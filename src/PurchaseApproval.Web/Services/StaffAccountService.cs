// Minimal administrator-managed lifecycle for administrator and accountant accounts.
//
// Responsibilities: create an account with a one-time temporary password, deactivate/reactivate,
// change the single role, initiate a password reset, and let staff change their own password.
// Every change writes an AuditEntry in the same transaction and refreshes the Identity security
// stamp so existing browser sessions stop working. Temporary passwords are returned once to the
// caller and never stored or logged in clear text. Related: Program.cs (cookie validation),
// Pages/Admin/Staff, Pages/Account/ChangePassword, ForcedPasswordChangeFilter.
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Services;

public sealed record StaffAccountRow(
    int Id,
    string Username,
    string FullName,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? TemporaryCredentialExpiresAtUtc,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeactivatedAtUtc,
    string? Department,
    string? Branch,
    string? JobTitle);

public sealed record StaffAccountInput(
    string Username,
    string FullName,
    string Role,
    string? Department,
    string? Branch,
    string? JobTitle);

public enum StaffAccountOperation
{
    Success = 1,
    NotFound = 2,
    LastActiveAdministrator = 3,
    InvalidRole = 4,
    SelfAction = 5,
    NoChange = 6,
    ValidationFailed = 7
}

public sealed record StaffOperationResult(
    StaffAccountOperation Status,
    IReadOnlyList<LocalizableMessage> Errors)
{
    public static readonly StaffOperationResult Success = new(StaffAccountOperation.Success, []);

    public static StaffOperationResult Fail(StaffAccountOperation status, params LocalizableMessage[] errors)
        => new(status, errors);
}

/// <summary>Result of an operation that issues a temporary password (creation or reset).</summary>
public sealed record StaffCredentialResult(
    StaffAccountOperation Status,
    int? UserId,
    string? TemporaryPassword,
    DateTimeOffset? ExpiresAtUtc,
    IReadOnlyList<LocalizableMessage> Errors);

public interface IStaffAccountService
{
    Task<IReadOnlyList<StaffAccountRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<StaffAccountRow?> GetAsync(int userId, CancellationToken cancellationToken = default);

    Task<StaffCredentialResult> CreateAsync(
        StaffAccountInput input, AuditActor actor, CancellationToken cancellationToken = default);

    Task<StaffOperationResult> DeactivateAsync(
        int userId, AuditActor actor, CancellationToken cancellationToken = default);

    Task<StaffOperationResult> ReactivateAsync(
        int userId, AuditActor actor, CancellationToken cancellationToken = default);

    Task<StaffOperationResult> ChangeRoleAsync(
        int userId, string newRole, AuditActor actor, CancellationToken cancellationToken = default);

    Task<StaffCredentialResult> InitiatePasswordResetAsync(
        int userId, AuditActor actor, CancellationToken cancellationToken = default);

    Task<StaffOperationResult> ChangeOwnPasswordAsync(
        int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

public sealed class StaffAccountService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    IOptions<ApplicationLimitsOptions> limits) : IStaffAccountService
{
    // Arbitrary constant for pg_advisory_xact_lock: serializes staff-account changes so the
    // "last active administrator" check cannot race with a concurrent deactivation.
    private const long StaffAccountLockKey = 7201;

    public async Task<IReadOnlyList<StaffAccountRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<StaffAccountRow>();
        foreach (var role in AppRoles.All)
        {
            var users = await userManager.GetUsersInRoleAsync(role);
            rows.AddRange(users.Select(user => ToRow(user, role)));
        }

        return rows.OrderBy(row => row.Role).ThenBy(row => row.Username, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<StaffAccountRow?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return ToRow(user, roles.FirstOrDefault() ?? string.Empty);
    }

    public async Task<StaffCredentialResult> CreateAsync(
        StaffAccountInput input,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!AppRoles.All.Contains(input.Role, StringComparer.Ordinal))
        {
            return CredentialFailure(StaffAccountOperation.InvalidRole, "Choose Administrator or Accountant.");
        }

        var now = timeProvider.GetUtcNow();
        var temporaryPassword = GenerateTemporaryPassword();
        var expiresAt = now.AddHours(limits.Value.TemporaryCredentialLifetimeHours);
        var user = new ApplicationUser
        {
            UserName = input.Username.Trim(),
            FullName = input.FullName.Trim(),
            Department = Clean(input.Department),
            Branch = Clean(input.Branch),
            JobTitle = Clean(input.JobTitle),
            IsActive = true,
            CreatedAtUtc = now,
            MustChangePassword = true,
            TemporaryCredentialExpiresAtUtc = expiresAt
        };

        await using var transaction = await BeginAsync(cancellationToken);
        var created = await userManager.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded)
        {
            await RollbackAsync(transaction, cancellationToken);
            return CredentialFailure(StaffAccountOperation.ValidationFailed, created.Errors);
        }

        var roleResult = await userManager.AddToRoleAsync(user, input.Role);
        if (!roleResult.Succeeded)
        {
            await RollbackAsync(transaction, cancellationToken);
            return CredentialFailure(StaffAccountOperation.ValidationFailed, roleResult.Errors);
        }

        dbContext.AuditEntries.Add(AuditRecorder.Create(
            AuditCategory.StaffAccount,
            AuditAction.StaffAccountCreated,
            nameof(ApplicationUser),
            user.Id,
            user.UserName!,
            actor,
            now,
            $"Role {input.Role}; temporary password issued, expires {expiresAt:u}."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new StaffCredentialResult(StaffAccountOperation.Success, user.Id, temporaryPassword, expiresAt, []);
    }

    public async Task<StaffOperationResult> DeactivateAsync(
        int userId,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        if (actor.UserId == userId)
        {
            return StaffOperationResult.Fail(StaffAccountOperation.SelfAction,
                "You cannot deactivate your own account.");
        }

        await using var transaction = await BeginAsync(cancellationToken);
        await LockStaffAccountsAsync(cancellationToken);

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.NotFound, "The account was not found.");
        }

        if (!user.IsActive)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.NoChange, "The account is already deactivated.");
        }

        if (await userManager.IsInRoleAsync(user, AppRoles.Administrator)
            && await CountOtherActiveAdministratorsAsync(user.Id) == 0)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.LastActiveAdministrator,
                "This is the last active administrator and cannot be deactivated.");
        }

        var now = timeProvider.GetUtcNow();
        user.IsActive = false;
        user.DeactivatedAtUtc = now;
        // A new security stamp makes every existing cookie for this account invalid.
        await userManager.UpdateSecurityStampAsync(user);
        await userManager.UpdateAsync(user);
        dbContext.AuditEntries.Add(AuditRecorder.Create(
            AuditCategory.StaffAccount, AuditAction.StaffAccountDeactivated,
            nameof(ApplicationUser), user.Id, user.UserName!, actor, now,
            "Account deactivated; sessions invalidated."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return StaffOperationResult.Success;
    }

    public async Task<StaffOperationResult> ReactivateAsync(
        int userId,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.NotFound, "The account was not found.");
        }

        if (user.IsActive)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.NoChange, "The account is already active.");
        }

        var now = timeProvider.GetUtcNow();
        user.IsActive = true;
        user.DeactivatedAtUtc = null;
        // Reactivation never resurrects old sessions and never clears an outstanding reset:
        // MustChangePassword and the temporary-credential expiry are left exactly as they were.
        await userManager.UpdateSecurityStampAsync(user);
        await userManager.UpdateAsync(user);
        dbContext.AuditEntries.Add(AuditRecorder.Create(
            AuditCategory.StaffAccount, AuditAction.StaffAccountReactivated,
            nameof(ApplicationUser), user.Id, user.UserName!, actor, now,
            user.MustChangePassword
                ? "Account reactivated; an outstanding password setup/reset is still required."
                : "Account reactivated."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return StaffOperationResult.Success;
    }

    public async Task<StaffOperationResult> ChangeRoleAsync(
        int userId,
        string newRole,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        if (!AppRoles.All.Contains(newRole, StringComparer.Ordinal))
        {
            return StaffOperationResult.Fail(StaffAccountOperation.InvalidRole, "Choose Administrator or Accountant.");
        }

        if (actor.UserId == userId)
        {
            return StaffOperationResult.Fail(StaffAccountOperation.SelfAction,
                "You cannot change your own role.");
        }

        await using var transaction = await BeginAsync(cancellationToken);
        await LockStaffAccountsAsync(cancellationToken);

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.NotFound, "The account was not found.");
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentRole = currentRoles.FirstOrDefault();
        if (currentRole == newRole)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.NoChange, "The account already has that role.");
        }

        if (currentRole == AppRoles.Administrator && user.IsActive
            && await CountOtherActiveAdministratorsAsync(user.Id) == 0)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.LastActiveAdministrator,
                "This is the last active administrator and cannot be demoted.");
        }

        var removed = await userManager.RemoveFromRolesAsync(user, currentRoles);
        var added = await userManager.AddToRoleAsync(user, newRole);
        if (!removed.Succeeded || !added.Succeeded)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.ValidationFailed,
                removed.Errors.Concat(added.Errors).Select(error => new LocalizableMessage(error.Description)).ToArray());
        }

        var now = timeProvider.GetUtcNow();
        // Role claims live in the cookie, so the holder must sign in again to receive the new role.
        await userManager.UpdateSecurityStampAsync(user);
        dbContext.AuditEntries.Add(AuditRecorder.Create(
            AuditCategory.StaffAccount, AuditAction.StaffRoleChanged,
            nameof(ApplicationUser), user.Id, user.UserName!, actor, now,
            $"Role changed from {currentRole ?? "(none)"} to {newRole}; sessions invalidated."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return StaffOperationResult.Success;
    }

    public async Task<StaffCredentialResult> InitiatePasswordResetAsync(
        int userId,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        if (actor.UserId == userId)
        {
            return CredentialFailure(StaffAccountOperation.SelfAction,
                "Use the change-password page for your own account.");
        }

        await using var transaction = await BeginAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            await RollbackAsync(transaction, cancellationToken);
            return CredentialFailure(StaffAccountOperation.NotFound, "The account was not found.");
        }

        var now = timeProvider.GetUtcNow();
        var temporaryPassword = GenerateTemporaryPassword();
        var expiresAt = now.AddHours(limits.Value.TemporaryCredentialLifetimeHours);

        // ResetPasswordAsync replaces the hash and rotates the security stamp, which signs out
        // every existing session and invalidates any earlier temporary password.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!reset.Succeeded)
        {
            await RollbackAsync(transaction, cancellationToken);
            return CredentialFailure(StaffAccountOperation.ValidationFailed, reset.Errors);
        }

        user.MustChangePassword = true;
        user.TemporaryCredentialExpiresAtUtc = expiresAt;
        await userManager.UpdateAsync(user);
        dbContext.AuditEntries.Add(AuditRecorder.Create(
            AuditCategory.StaffAccount, AuditAction.StaffPasswordResetInitiated,
            nameof(ApplicationUser), user.Id, user.UserName!, actor, now,
            $"Temporary password issued, expires {expiresAt:u}; previous sessions invalidated."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return new StaffCredentialResult(StaffAccountOperation.Success, user.Id, temporaryPassword, expiresAt, []);
    }

    public async Task<StaffOperationResult> ChangeOwnPasswordAsync(
        int userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.NotFound, "The account was not found.");
        }

        var now = timeProvider.GetUtcNow();
        if (user.MustChangePassword && user.TemporaryCredentialExpiresAtUtc is { } expiry && expiry < now)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.ValidationFailed,
                "The temporary password has expired. Ask an administrator to initiate a new reset.");
        }

        var wasSetup = user.MustChangePassword;
        var changed = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
        {
            await RollbackAsync(transaction, cancellationToken);
            return StaffOperationResult.Fail(StaffAccountOperation.ValidationFailed,
                changed.Errors.Select(error => new LocalizableMessage(error.Description)).ToArray());
        }

        user.MustChangePassword = false;
        user.TemporaryCredentialExpiresAtUtc = null;
        user.PasswordChangedAtUtc = now;
        await userManager.UpdateAsync(user);
        var roles = await userManager.GetRolesAsync(user);
        dbContext.AuditEntries.Add(AuditRecorder.Create(
            AuditCategory.StaffAccount,
            wasSetup ? AuditAction.StaffPasswordSetupCompleted : AuditAction.StaffPasswordChanged,
            nameof(ApplicationUser), user.Id, user.UserName!,
            new AuditActor(user.Id, user.FullName, roles.FirstOrDefault() ?? string.Empty), now,
            wasSetup ? "Temporary password replaced by a personal password." : "Password changed by the account holder."));
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return StaffOperationResult.Success;
    }

    private async Task<int> CountOtherActiveAdministratorsAsync(int excludedUserId)
    {
        var administrators = await userManager.GetUsersInRoleAsync(AppRoles.Administrator);
        return administrators.Count(user => user.IsActive && user.Id != excludedUserId);
    }

    private async Task LockStaffAccountsAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock({StaffAccountLockKey})", cancellationToken);
        }
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(
        CancellationToken cancellationToken)
        => dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static async Task CommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static StaffCredentialResult CredentialFailure(StaffAccountOperation status, LocalizableMessage message)
        => new(status, null, null, null, [message]);

    private static StaffCredentialResult CredentialFailure(StaffAccountOperation status, IEnumerable<IdentityError> errors)
        => new(status, null, null, null, errors.Select(error => new LocalizableMessage(error.Description)).ToList());

    private static StaffAccountRow ToRow(ApplicationUser user, string role) => new(
        user.Id,
        user.UserName ?? string.Empty,
        user.FullName,
        role,
        user.IsActive,
        user.MustChangePassword,
        user.TemporaryCredentialExpiresAtUtc,
        user.LastAuthenticatedAtUtc,
        user.CreatedAtUtc,
        user.DeactivatedAtUtc,
        user.Department,
        user.Branch,
        user.JobTitle);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 16 random characters guaranteed to satisfy the Identity password policy (upper, lower,
    /// digit, symbol). Ambiguous glyphs such as 0/O and 1/l are excluded so a hand-delivered
    /// password can be typed reliably.
    /// </summary>
    public static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%&*?";
        const string all = upper + lower + digits + symbols;

        var characters = new List<char>
        {
            Pick(upper), Pick(lower), Pick(digits), Pick(symbols)
        };
        while (characters.Count < 16)
        {
            characters.Add(Pick(all));
        }

        // Fisher-Yates shuffle with a cryptographic source so the class order is unpredictable.
        for (var index = characters.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swap]) = (characters[swap], characters[index]);
        }

        return new string(characters.ToArray());

        static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }
}
