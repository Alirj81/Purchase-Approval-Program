// Verifies login throttling counters, database-error classification and self-approval auditing.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class SecurityRulesTests
{
    [Fact]
    public void LoginThrottling_BlocksByIpAndByAccount_AndSuccessClearsOnlyTheAccount()
    {
        var service = new LoginThrottlingService(
            new MemoryCache(new MemoryCacheOptions()),
            TestSupport.Limits(options =>
            {
                options.LoginThrottling.FailuresPerIp = 3;
                options.LoginThrottling.FailuresPerAccount = 2;
            }));
        var account = ILoginThrottlingService.AccountKey("employee", "a.rahimi");
        var other = ILoginThrottlingService.AccountKey("employee", "b.karimi");

        service.RecordFailure("10.0.0.5", account);
        service.RecordFailure("10.0.0.5", account);
        Assert.True(service.IsBlocked("10.0.0.5", account));
        // A shared office address is not blocked for another account until the IP limit is hit.
        Assert.False(service.IsBlocked("10.0.0.5", other));

        service.RecordSuccess("10.0.0.5", account);
        Assert.False(service.IsBlocked("10.0.0.5", account));

        service.RecordFailure("10.0.0.5", other);
        Assert.True(service.IsBlocked("10.0.0.5", account));
        Assert.True(service.IsBlocked("10.0.0.5", other));
        Assert.False(service.IsBlocked("10.0.0.6", other));
    }

    [Fact]
    public void AccountKey_NormalizesUsernamesAndNeverContainsSecrets()
    {
        Assert.Equal("staff:ADMIN", ILoginThrottlingService.AccountKey("staff", "  admin "));
        Assert.Equal("employee:", ILoginThrottlingService.AccountKey("employee", null));
    }

    [Fact]
    public void DatabaseErrorClassifier_DistinguishesConcurrencyUniqueConstraintAndUnexpected()
    {
        var concurrency = DatabaseErrorClassifier.Classify(new DbUpdateConcurrencyException("stale"));
        var unique = DatabaseErrorClassifier.Classify(new DbUpdateException("dup", Postgres("23505", "IX_TransferOutcomes_PaymentRequestId")));
        var check = DatabaseErrorClassifier.Classify(new DbUpdateException("chk", Postgres("23514", "CK_TransferOutcomes_ClosureState")));
        var unexpected = DatabaseErrorClassifier.Classify(new DbUpdateException("boom", new InvalidOperationException()));

        Assert.Equal(DatabaseFailureKind.ConcurrencyConflict, concurrency.Kind);
        Assert.Equal(DatabaseFailureKind.UniqueViolation, unique.Kind);
        Assert.Equal("IX_TransferOutcomes_PaymentRequestId", unique.ConstraintName);
        Assert.Equal(DatabaseFailureKind.ConstraintViolation, check.Kind);
        Assert.Equal(DatabaseFailureKind.Unexpected, unexpected.Kind);
    }

    [Fact]
    public void SelfApproval_IsDerivedFromIdentitiesAndAudited()
    {
        var ownRequest = new PaymentRequest
        {
            Id = 5,
            ReferenceNumber = "PR-SELF",
            RequesterType = RequesterType.Administrator,
            ApplicationUserRequesterId = 7,
            Status = PaymentRequestStatus.Pending
        };
        var employeeRequest = new PaymentRequest
        {
            Id = 6,
            ReferenceNumber = "PR-EMP",
            RequesterType = RequesterType.Employee,
            EmployeeRequesterId = 7,
            Status = PaymentRequestStatus.Pending
        };
        var when = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        var self = ApprovalDecisionRules.CreateDecision(ownRequest, 7, "Mina", ApprovalDecisionType.Approved, null, when);
        var normal = ApprovalDecisionRules.CreateDecision(employeeRequest, 7, "Mina", ApprovalDecisionType.Approved, null, when);
        var selfAudit = ApprovalDecisionRules.CreateAudit(ownRequest, self);
        var normalAudit = ApprovalDecisionRules.CreateAudit(employeeRequest, normal);

        Assert.True(self.IsSelfApproval);
        // Same numeric ID but an employee requester is not a self-approval.
        Assert.False(normal.IsSelfApproval);
        Assert.Equal(AuditAction.RequestSelfApproved, selfAudit.Action);
        Assert.Equal(AuditAction.RequestApproved, normalAudit.Action);
        Assert.Equal(AuditCategory.ApprovalDecision, selfAudit.Category);
        Assert.Equal("PR-SELF", selfAudit.SubjectLabel);
        Assert.Equal(7, selfAudit.ActorUserId);
    }

    [Fact]
    public void TemporaryPassword_SatisfiesThePasswordPolicyAndVaries()
    {
        var first = StaffAccountService.GenerateTemporaryPassword();
        var second = StaffAccountService.GenerateTemporaryPassword();

        Assert.Equal(16, first.Length);
        Assert.NotEqual(first, second);
        Assert.Contains(first, char.IsUpper);
        Assert.Contains(first, char.IsLower);
        Assert.Contains(first, char.IsDigit);
        Assert.Contains(first, character => !char.IsLetterOrDigit(character));
    }

    private static PostgresException Postgres(string sqlState, string constraint)
        => new("message", "ERROR", "ERROR", sqlState, constraintName: constraint);
}
