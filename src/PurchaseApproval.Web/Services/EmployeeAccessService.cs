// Employee profile and credential lifecycle, separated into two kinds of operation:
//   account state  - Disable / Enable / Revoke / Restore (changes AccessStatus);
//   credential     - ReplaceKey (issues a new access key, never changes AccessStatus).
// The split is what stops "New key" from silently re-enabling a disabled or revoked employee.
// Every change bumps CredentialVersion when access must be cut, writes an AuditEntry in the same
// save, and returns the clear-text key at most once. Related: EmployeeAccessKeyService,
// EmployeeCookieEvents (session revalidation), Pages/Admin/Employees.
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public sealed record EmployeeProfileInput(
    string FullName,
    string? PhoneNumber,
    string? Department,
    string? Branch,
    string? JobTitle,
    string? AdminNotes);

public enum EmployeeAccessOperation
{
    Success = 1,
    NotFound = 2,
    UsernameTaken = 3,
    InvalidState = 4,
    ValidationFailed = 5
}

public sealed record EmployeeAccessResult(
    EmployeeAccessOperation Status,
    int? EmployeeId,
    string? IssuedAccessKey,
    IReadOnlyList<LocalizableMessage> Errors)
{
    public static EmployeeAccessResult Ok(int employeeId, string? key = null)
        => new(EmployeeAccessOperation.Success, employeeId, key, []);

    public static EmployeeAccessResult Fail(EmployeeAccessOperation status, params LocalizableMessage[] errors)
        => new(status, null, null, errors);
}

public interface IEmployeeAccessService
{
    Task<EmployeeAccessResult> CreateAsync(
        string username, EmployeeProfileInput profile, AuditActor actor, CancellationToken cancellationToken = default);

    Task<EmployeeAccessResult> UpdateProfileAsync(
        int employeeId, EmployeeProfileInput profile, AuditActor actor, CancellationToken cancellationToken = default);

    Task<EmployeeAccessResult> DisableAsync(int employeeId, AuditActor actor, CancellationToken cancellationToken = default);

    Task<EmployeeAccessResult> EnableAsync(int employeeId, AuditActor actor, CancellationToken cancellationToken = default);

    Task<EmployeeAccessResult> RevokeAsync(int employeeId, AuditActor actor, CancellationToken cancellationToken = default);

    /// <summary>Explicit restoration of a revoked employee: sets Active and issues a fresh key.</summary>
    Task<EmployeeAccessResult> RestoreAccessAsync(int employeeId, AuditActor actor, CancellationToken cancellationToken = default);

    /// <summary>Issues a replacement key without changing the account state; refused for revoked employees.</summary>
    Task<EmployeeAccessResult> ReplaceKeyAsync(int employeeId, AuditActor actor, CancellationToken cancellationToken = default);
}

public sealed class EmployeeAccessService(
    ApplicationDbContext dbContext,
    IEmployeeAccessKeyService accessKeyService,
    ILookupNormalizer lookupNormalizer,
    TimeProvider timeProvider,
    IOptions<ApplicationLimitsOptions> limits) : IEmployeeAccessService
{
    public async Task<EmployeeAccessResult> CreateAsync(
        string username,
        EmployeeProfileInput profile,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProfile(profile);
        var cleanUsername = username?.Trim() ?? string.Empty;
        if (cleanUsername.Length is < 3 or > 100)
        {
            errors.Add("Username must be between 3 and 100 characters.");
        }

        if (errors.Count != 0)
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.ValidationFailed, errors.ToArray());
        }

        var normalizedUsername = lookupNormalizer.NormalizeName(cleanUsername);
        if (await dbContext.Employees.AnyAsync(item => item.NormalizedUsername == normalizedUsername, cancellationToken))
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.UsernameTaken, "This username is already in use.");
        }

        var now = timeProvider.GetUtcNow();
        var employee = new Employee
        {
            FullName = profile.FullName.Trim(),
            Username = cleanUsername,
            NormalizedUsername = normalizedUsername,
            PhoneNumber = Clean(profile.PhoneNumber),
            Department = Clean(profile.Department),
            Branch = Clean(profile.Branch),
            JobTitle = Clean(profile.JobTitle),
            AdminNotes = Clean(profile.AdminNotes),
            AccessKeyHash = "pending",
            AccessStatus = EmployeeAccessStatus.Active,
            CreatedAtUtc = now
        };

        // The key embeds the database ID, so the row must exist before the key can be built.
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);

        var key = accessKeyService.InitializeCredential(employee);
        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeCreated, employee, actor, now,
            "Employee created; first access key issued."));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return EmployeeAccessResult.Ok(employee.Id, key);
    }

    public async Task<EmployeeAccessResult> UpdateProfileAsync(
        int employeeId,
        EmployeeProfileInput profile,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProfile(profile);
        if (errors.Count != 0)
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.ValidationFailed, errors.ToArray());
        }

        var employee = await dbContext.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        var changes = new List<string>();
        Apply(changes, "Full name", employee.FullName, profile.FullName.Trim(), value => employee.FullName = value);
        Apply(changes, "Phone", employee.PhoneNumber, Clean(profile.PhoneNumber), value => employee.PhoneNumber = value);
        Apply(changes, "Department", employee.Department, Clean(profile.Department), value => employee.Department = value);
        Apply(changes, "Branch", employee.Branch, Clean(profile.Branch), value => employee.Branch = value);
        Apply(changes, "Job title", employee.JobTitle, Clean(profile.JobTitle), value => employee.JobTitle = value);
        // Notes are administrator-only; the audit records that they changed, not their content.
        if (!string.Equals(employee.AdminNotes, Clean(profile.AdminNotes), StringComparison.Ordinal))
        {
            employee.AdminNotes = Clean(profile.AdminNotes);
            changes.Add("Administrator notes updated");
        }

        if (changes.Count == 0)
        {
            return EmployeeAccessResult.Ok(employee.Id);
        }

        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeProfileUpdated, employee, actor,
            timeProvider.GetUtcNow(), string.Join("; ", changes)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return EmployeeAccessResult.Ok(employee.Id);
    }

    public async Task<EmployeeAccessResult> DisableAsync(
        int employeeId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var employee = await dbContext.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (employee.AccessStatus != EmployeeAccessStatus.Active)
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.InvalidState,
                "Only an active employee can be disabled.");
        }

        var now = timeProvider.GetUtcNow();
        employee.AccessStatus = EmployeeAccessStatus.Disabled;
        employee.DisabledAtUtc = now;
        // Bumping the version makes EmployeeCookieEvents reject every existing session.
        employee.CredentialVersion = checked(employee.CredentialVersion + 1);
        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeDisabled, employee, actor, now,
            "Access disabled; sessions invalidated; the existing key is suspended, not replaced."));
        await dbContext.SaveChangesAsync(cancellationToken);
        return EmployeeAccessResult.Ok(employee.Id);
    }

    public async Task<EmployeeAccessResult> EnableAsync(
        int employeeId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var employee = await dbContext.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (employee.AccessStatus != EmployeeAccessStatus.Disabled)
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.InvalidState,
                "Only a disabled employee can be enabled. A revoked employee needs explicit restoration.");
        }

        var now = timeProvider.GetUtcNow();
        employee.AccessStatus = EmployeeAccessStatus.Active;
        employee.DisabledAtUtc = null;
        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeEnabled, employee, actor, now,
            "Access enabled; the existing key works again, previous sessions stay invalid."));
        await dbContext.SaveChangesAsync(cancellationToken);
        return EmployeeAccessResult.Ok(employee.Id);
    }

    public async Task<EmployeeAccessResult> RevokeAsync(
        int employeeId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var employee = await dbContext.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (employee.AccessStatus == EmployeeAccessStatus.Revoked)
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.InvalidState, "Access is already revoked.");
        }

        var now = timeProvider.GetUtcNow();
        employee.AccessStatus = EmployeeAccessStatus.Revoked;
        employee.RevokedAtUtc = now;
        employee.DisabledAtUtc = null;
        employee.CredentialVersion = checked(employee.CredentialVersion + 1);
        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeRevoked, employee, actor, now,
            "Access revoked; key and sessions invalidated. Restoration requires an explicit action and a fresh key."));
        await dbContext.SaveChangesAsync(cancellationToken);
        return EmployeeAccessResult.Ok(employee.Id);
    }

    public async Task<EmployeeAccessResult> RestoreAccessAsync(
        int employeeId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var employee = await dbContext.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (employee.AccessStatus != EmployeeAccessStatus.Revoked)
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.InvalidState,
                "Only a revoked employee can be restored.");
        }

        var now = timeProvider.GetUtcNow();
        // The previous secret is never brought back: restoration always issues a fresh key.
        var key = accessKeyService.RotateCredential(employee);
        employee.AccessStatus = EmployeeAccessStatus.Active;
        employee.RevokedAtUtc = null;
        employee.DisabledAtUtc = null;
        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeAccessRestored, employee, actor, now,
            "Revoked access restored by explicit administrator confirmation."));
        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeKeyReplaced, employee, actor, now,
            "Fresh access key issued during restoration."));
        await dbContext.SaveChangesAsync(cancellationToken);
        return EmployeeAccessResult.Ok(employee.Id, key);
    }

    public async Task<EmployeeAccessResult> ReplaceKeyAsync(
        int employeeId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var employee = await dbContext.Employees.FindAsync([employeeId], cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (employee.AccessStatus == EmployeeAccessStatus.Revoked)
        {
            return EmployeeAccessResult.Fail(EmployeeAccessOperation.InvalidState,
                "Restore the revoked employee first; a replacement key alone does not restore access.");
        }

        var now = timeProvider.GetUtcNow();
        var key = accessKeyService.RotateCredential(employee);
        dbContext.AuditEntries.Add(Audit(AuditAction.EmployeeKeyReplaced, employee, actor, now,
            $"Access key replaced; account state remains {employee.AccessStatus}; previous key and sessions invalidated."));
        await dbContext.SaveChangesAsync(cancellationToken);
        return EmployeeAccessResult.Ok(employee.Id, key);
    }

    private List<LocalizableMessage> ValidateProfile(EmployeeProfileInput profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<LocalizableMessage>();
        var fullName = profile.FullName?.Trim() ?? string.Empty;
        if (fullName.Length is 0 or > 200)
        {
            errors.Add("Full name is required and must not exceed 200 characters.");
        }

        if (limits.Value.RequireEmployeeBranch && string.IsNullOrWhiteSpace(profile.Branch))
        {
            errors.Add("Branch is required for this installation.");
        }

        if (Clean(profile.PhoneNumber)?.Length > 50) errors.Add("Phone number is too long.");
        if (Clean(profile.Department)?.Length > 100) errors.Add("Department is too long.");
        if (Clean(profile.Branch)?.Length > 100) errors.Add("Branch is too long.");
        if (Clean(profile.JobTitle)?.Length > 100) errors.Add("Job title is too long.");
        if (Clean(profile.AdminNotes)?.Length > 1000) errors.Add("Administrator notes are too long.");
        return errors;
    }

    private static void Apply(
        List<string> changes, string label, string? oldValue, string? newValue, Action<string> setter)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        setter(newValue ?? string.Empty);
        changes.Add($"{label}: '{oldValue ?? string.Empty}' -> '{newValue ?? string.Empty}'");
    }

    private static AuditEntry Audit(
        AuditAction action, Employee employee, AuditActor actor, DateTimeOffset now, string details)
        => AuditRecorder.Create(
            AuditCategory.EmployeeAccess, action, nameof(Employee), employee.Id, employee.Username, actor, now, details);

    private static EmployeeAccessResult NotFound()
        => EmployeeAccessResult.Fail(EmployeeAccessOperation.NotFound, "The employee was not found.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
