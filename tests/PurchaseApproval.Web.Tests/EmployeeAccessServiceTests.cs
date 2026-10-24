// Verifies the separation between account state and credential operations for employees:
// a replacement key never changes the state, revoked employees need explicit restoration with a
// fresh key, and every change leaves an audit row without the secret.
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class EmployeeAccessServiceTests
{
    private static readonly AuditActor Admin = new(1, "Mina Manager", "Administrator");

    [Fact]
    public async Task Create_IssuesVerifiableKeyAndAudits()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var (service, keys) = CreateService(dbContext);

        var result = await service.CreateAsync("a.rahimi", Profile("Ali Rahimi"), Admin);

        Assert.Equal(EmployeeAccessOperation.Success, result.Status);
        var employee = await dbContext.Employees.SingleAsync();
        Assert.True(keys.Verify(employee, result.IssuedAccessKey!));
        Assert.Equal("A.RAHIMI", employee.NormalizedUsername);
        var audit = Assert.Single(dbContext.AuditEntries);
        Assert.Equal(AuditAction.EmployeeCreated, audit.Action);
        Assert.DoesNotContain(result.IssuedAccessKey!, audit.Details ?? string.Empty);
    }

    [Fact]
    public async Task Create_RejectsDuplicateUsernameAndMissingBranchWhenRequired()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var (service, _) = CreateService(dbContext, requireBranch: true);
        await service.CreateAsync("a.rahimi", Profile("Ali Rahimi", branch: "Main"), Admin);

        var duplicate = await service.CreateAsync("A.Rahimi", Profile("Other", branch: "Main"), Admin);
        var noBranch = await service.CreateAsync("b.karimi", Profile("Bita Karimi"), Admin);

        Assert.Equal(EmployeeAccessOperation.UsernameTaken, duplicate.Status);
        Assert.Equal(EmployeeAccessOperation.ValidationFailed, noBranch.Status);
        Assert.Contains(noBranch.Errors, error => error.Key.Contains("Branch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisableAndEnable_KeepTheKeyButEndSessions()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var (service, keys) = CreateService(dbContext);
        var created = await service.CreateAsync("a.rahimi", Profile("Ali"), Admin);
        var employee = await dbContext.Employees.SingleAsync();
        var versionBefore = employee.CredentialVersion;

        Assert.Equal(EmployeeAccessOperation.Success, (await service.DisableAsync(employee.Id, Admin)).Status);
        Assert.Equal(EmployeeAccessStatus.Disabled, employee.AccessStatus);
        Assert.Equal(versionBefore + 1, employee.CredentialVersion);

        Assert.Equal(EmployeeAccessOperation.Success, (await service.EnableAsync(employee.Id, Admin)).Status);
        Assert.Equal(EmployeeAccessStatus.Active, employee.AccessStatus);
        // The original key still works after enabling; the version bump only ended old sessions.
        Assert.True(keys.Verify(employee, created.IssuedAccessKey!));
        Assert.Equal(EmployeeAccessOperation.InvalidState, (await service.EnableAsync(employee.Id, Admin)).Status);
    }

    [Fact]
    public async Task ReplaceKey_DoesNotChangeAccountStateAndInvalidatesOldKey()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var (service, keys) = CreateService(dbContext);
        var created = await service.CreateAsync("a.rahimi", Profile("Ali"), Admin);
        var employee = await dbContext.Employees.SingleAsync();
        await service.DisableAsync(employee.Id, Admin);

        var replaced = await service.ReplaceKeyAsync(employee.Id, Admin);

        Assert.Equal(EmployeeAccessOperation.Success, replaced.Status);
        Assert.Equal(EmployeeAccessStatus.Disabled, employee.AccessStatus);
        Assert.False(keys.Verify(employee, created.IssuedAccessKey!));
        Assert.True(keys.Verify(employee, replaced.IssuedAccessKey!));
    }

    [Fact]
    public async Task Revoke_ThenReplaceKeyIsRefused_AndRestoreIssuesFreshKey()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var (service, keys) = CreateService(dbContext);
        var created = await service.CreateAsync("a.rahimi", Profile("Ali"), Admin);
        var employee = await dbContext.Employees.SingleAsync();

        Assert.Equal(EmployeeAccessOperation.Success, (await service.RevokeAsync(employee.Id, Admin)).Status);
        Assert.Equal(EmployeeAccessStatus.Revoked, employee.AccessStatus);
        Assert.Equal(EmployeeAccessOperation.InvalidState, (await service.ReplaceKeyAsync(employee.Id, Admin)).Status);
        Assert.Equal(EmployeeAccessOperation.InvalidState, (await service.EnableAsync(employee.Id, Admin)).Status);
        Assert.Equal(EmployeeAccessStatus.Revoked, employee.AccessStatus);

        var restored = await service.RestoreAccessAsync(employee.Id, Admin);

        Assert.Equal(EmployeeAccessOperation.Success, restored.Status);
        Assert.Equal(EmployeeAccessStatus.Active, employee.AccessStatus);
        Assert.Null(employee.RevokedAtUtc);
        Assert.False(keys.Verify(employee, created.IssuedAccessKey!));
        Assert.True(keys.Verify(employee, restored.IssuedAccessKey!));
        var actions = await dbContext.AuditEntries.Select(entry => entry.Action).ToListAsync();
        Assert.Contains(AuditAction.EmployeeRevoked, actions);
        Assert.Contains(AuditAction.EmployeeAccessRestored, actions);
        Assert.Contains(AuditAction.EmployeeKeyReplaced, actions);
    }

    [Fact]
    public async Task UpdateProfile_AuditsChangedFieldsWithoutExposingNotes()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var (service, keys) = CreateService(dbContext);
        var created = await service.CreateAsync("a.rahimi", Profile("Ali"), Admin);
        var employee = await dbContext.Employees.SingleAsync();

        var result = await service.UpdateProfileAsync(
            employee.Id, Profile("Ali Rahimi", department: "Sales", notes: "secret note"), Admin);

        Assert.Equal(EmployeeAccessOperation.Success, result.Status);
        Assert.Equal("Ali Rahimi", employee.FullName);
        Assert.Equal("secret note", employee.AdminNotes);
        Assert.NotNull(result.IssuedAccessKey);
        Assert.False(keys.Verify(employee, created.IssuedAccessKey!));
        Assert.True(keys.Verify(employee, result.IssuedAccessKey!));
        var audit = await dbContext.AuditEntries.SingleAsync(entry => entry.Action == AuditAction.EmployeeProfileUpdated);
        Assert.Contains("Full name", audit.Details);
        Assert.DoesNotContain("secret note", audit.Details);
    }

    [Fact]
    public async Task Delete_RemovesAccessButKeepsEmployeeAndHistoryIdentity()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var (service, keys) = CreateService(dbContext);
        var created = await service.CreateAsync("a.rahimi", Profile("Ali"), Admin);
        var employee = await dbContext.Employees.SingleAsync();

        var deleted = await service.DeleteAsync(employee.Id, Admin);

        Assert.Equal(EmployeeAccessOperation.Success, deleted.Status);
        Assert.Equal(EmployeeAccessStatus.Deleted, employee.AccessStatus);
        Assert.NotNull(employee.DeletedAtUtc);
        Assert.False(keys.Verify(employee, created.IssuedAccessKey!));
        Assert.Equal(employee.Id, (await dbContext.Employees.SingleAsync()).Id);
        Assert.Contains(await dbContext.AuditEntries.ToListAsync(), row => row.Action == AuditAction.EmployeeDeleted);
        Assert.Equal(EmployeeAccessOperation.InvalidState, (await service.ReplaceKeyAsync(employee.Id, Admin)).Status);
    }

    private static (EmployeeAccessService Service, EmployeeAccessKeyService Keys) CreateService(
        PurchaseApproval.Data.ApplicationDbContext dbContext,
        bool requireBranch = false)
    {
        var keys = new EmployeeAccessKeyService(new PasswordHasher<Employee>());
        var service = new EmployeeAccessService(
            dbContext,
            keys,
            new UpperInvariantLookupNormalizer(),
            new FixedClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)),
            TestSupport.Limits(options => options.RequireEmployeeBranch = requireBranch));
        return (service, keys);
    }

    private static EmployeeProfileInput Profile(
        string fullName,
        string? department = null,
        string? branch = null,
        string? notes = null)
        => new(fullName, null, department, branch, null, notes);
}
