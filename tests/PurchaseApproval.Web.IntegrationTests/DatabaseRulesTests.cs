// Real-PostgreSQL rules that EF InMemory cannot prove: atomic quota reservation under
// concurrency, competing decisions, the last-administrator lock, closure catch-up with a fresh
// context after "downtime", and the check constraints that make closure reasons mandatory.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseRulesTests : IAsyncLifetime
{
    private TestWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsAvailable)
        {
            return;
        }

        await TestDatabase.ResetAsync();
        _factory = new TestWebApplicationFactory(new Dictionary<string, string>
        {
            ["Limits:DailySubmissionQuota"] = "10"
        });
        _ = _factory.Services; // forces host start so roles exist
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [PostgresFact]
    public async Task DailyQuota_IsAtomicUnderConcurrentSubmissions()
    {
        var employee = await _factory.CreateEmployeeAsync("quota.employee");
        var profile = new EmployeeProfile(employee.Id, "Quota Employee", employee.Username, null, null, null, null);

        var attempts = Enumerable.Range(0, 20).Select(async _ =>
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPaymentRequestSubmissionService>();
            try
            {
                await service.SubmitAsync(
                    profile,
                    [new PaymentRequestLineInput("Row", 1_000, false, null)],
                    [], "Payee", "Reason", null,
                    PaymentRequestSubmissionRules.CreateSubmissionKey());
                return true;
            }
            catch (SubmissionQuotaExceededException)
            {
                return false;
            }
        });
        var outcomes = await Task.WhenAll(attempts);

        await using var context = TestDatabase.CreateContext();
        Assert.Equal(10, outcomes.Count(success => success));
        Assert.Equal(10, outcomes.Count(success => !success));
        Assert.Equal(10, await context.PaymentRequests.CountAsync());
        var quota = await context.SubmissionQuotaDays.SingleAsync();
        Assert.Equal(10, quota.CreatedCount);
    }

    [PostgresFact]
    public async Task CompetingDecisions_ProduceExactlyOneDecisionAndOneAuditRow()
    {
        var adminId = await _factory.CreateStaffAsync("decider", AppRoles.Administrator, "Decider#Password123");
        var employee = await _factory.CreateEmployeeAsync("decide.employee");
        var requestId = await SubmitAsync(employee);
        uint version;
        await using (var context = TestDatabase.CreateContext())
        {
            version = await context.PaymentRequests.Where(item => item.Id == requestId).Select(item => item.Version).SingleAsync();
        }

        var decisions = await Task.WhenAll(
            DecideAsync(requestId, version, adminId, ApprovalDecisionType.Approved),
            DecideAsync(requestId, version, adminId, ApprovalDecisionType.Rejected));

        await using var verify = TestDatabase.CreateContext();
        Assert.Single(decisions, result => result == ApprovalDecisionResult.Success);
        Assert.Single(decisions, result => result is ApprovalDecisionResult.ConcurrencyConflict or ApprovalDecisionResult.AlreadyDecided);
        Assert.Equal(1, await verify.ApprovalDecisions.CountAsync());
        Assert.Equal(1, await verify.AuditEntries.CountAsync(entry => entry.Category == AuditCategory.ApprovalDecision));
    }

    [PostgresFact]
    public async Task SelfApproval_IsPersistedWithAnAuditRow()
    {
        var adminId = await _factory.CreateStaffAsync("self.admin", AppRoles.Administrator, "SelfAdmin#Password123", "Self Admin");
        int requestId;
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentRequestSubmissionService>();
            var result = await service.SubmitAsync(
                new ApplicationUserProfile(adminId, "Self Admin", "self.admin", null, null, null, null),
                [new PaymentRequestLineInput("Row", 5_000, false, null)], [], "Payee", "Reason", null,
                PaymentRequestSubmissionRules.CreateSubmissionKey());
            requestId = result.RequestId;
        }

        uint version;
        await using (var context = TestDatabase.CreateContext())
        {
            version = await context.PaymentRequests.Where(item => item.Id == requestId).Select(item => item.Version).SingleAsync();
        }

        Assert.Equal(ApprovalDecisionResult.Success, await DecideAsync(requestId, version, adminId, ApprovalDecisionType.Approved));

        await using var verify = TestDatabase.CreateContext();
        var decision = await verify.ApprovalDecisions.SingleAsync();
        Assert.True(decision.IsSelfApproval);
        var audit = await verify.AuditEntries.SingleAsync(entry => entry.Category == AuditCategory.ApprovalDecision);
        Assert.Equal(AuditAction.RequestSelfApproved, audit.Action);
        Assert.Equal(adminId, audit.ActorUserId);
    }

    [PostgresFact]
    public async Task LastActiveAdministrator_CannotBeDeactivatedEvenUnderConcurrency()
    {
        var first = await _factory.CreateStaffAsync("admin.one", AppRoles.Administrator, "AdminOne#Password123", "Admin One");
        var second = await _factory.CreateStaffAsync("admin.two", AppRoles.Administrator, "AdminTwo#Password123", "Admin Two");

        // Each administrator tries to deactivate the other at the same time.
        var results = await Task.WhenAll(
            DeactivateAsync(second, new AuditActor(first, "Admin One", AppRoles.Administrator)),
            DeactivateAsync(first, new AuditActor(second, "Admin Two", AppRoles.Administrator)));

        Assert.Single(results, result => result.Status == StaffAccountOperation.Success);
        Assert.Single(results, result => result.Status == StaffAccountOperation.LastActiveAdministrator);

        await using var verify = TestDatabase.CreateContext();
        Assert.Equal(1, await verify.Users.CountAsync(user => user.IsActive));
        Assert.Equal(1, await verify.AuditEntries.CountAsync(entry => entry.Action == AuditAction.StaffAccountDeactivated));
    }

    [PostgresFact]
    public async Task ClosureProcessor_ClosesOverdueItemsAfterRestart_AndIsIdempotent()
    {
        var accountantId = await _factory.CreateStaffAsync("closer.acct", AppRoles.Accountant, "Closer#Password123", "Closer");
        var employee = await _factory.CreateEmployeeAsync("closure.employee");
        var overdueId = await SubmitAsync(employee);
        var freshId = await SubmitAsync(employee);
        var now = DateTimeOffset.UtcNow;

        await using (var context = TestDatabase.CreateContext())
        {
            foreach (var (id, recordedAt, status) in new[]
                     {
                         (overdueId, now.AddHours(-25), TransferStatus.Transferred),
                         (freshId, now.AddHours(-1), TransferStatus.Transferred)
                     })
            {
                var request = await context.PaymentRequests.SingleAsync(item => item.Id == id);
                request.Status = PaymentRequestStatus.Approved;
                context.TransferOutcomes.Add(new TransferOutcome
                {
                    PaymentRequestId = id,
                    Status = status,
                    Method = TransferMethod.Cash,
                    PerformedByUserId = accountantId,
                    PerformedByFullNameSnapshot = "Closer",
                    PerformedByRoleSnapshot = AppRoles.Accountant,
                    TransactionOccurredAtUtc = recordedAt.AddMinutes(-5),
                    TransactionUtcOffsetMinutes = 0,
                    RecordedAtUtc = recordedAt,
                    IsClosed = false
                });
            }

            await context.SaveChangesAsync();
        }

        // A brand-new scope stands in for the application starting after downtime.
        int firstRun, secondRun;
        using (var scope = _factory.Services.CreateScope())
        {
            firstRun = await scope.ServiceProvider.GetRequiredService<ITransferClosureProcessor>().CloseOverdueAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            secondRun = await scope.ServiceProvider.GetRequiredService<ITransferClosureProcessor>().CloseOverdueAsync();
        }

        await using var verify = TestDatabase.CreateContext();
        var overdue = await verify.TransferOutcomes.Include(item => item.AuditEntries).SingleAsync(item => item.PaymentRequestId == overdueId);
        var fresh = await verify.TransferOutcomes.SingleAsync(item => item.PaymentRequestId == freshId);
        Assert.Equal(1, firstRun);
        Assert.Equal(0, secondRun);
        Assert.True(overdue.IsClosed);
        Assert.Equal(TransferStatus.Transferred, overdue.Status);
        Assert.Equal(TransferClosureType.Automatic, overdue.ClosureType);
        Assert.Equal(TransferExecutionRules.AutomaticTransferredClosureReason, overdue.ClosureReason);
        Assert.Contains(overdue.AuditEntries, audit => audit.Action == TransferAuditAction.TransferredClosedAutomatically);
        Assert.False(fresh.IsClosed);
        Assert.Equal(2, await verify.PaymentRequests.CountAsync());
    }

    [PostgresFact]
    public async Task Database_RejectsClosureWithoutReasonAndUnsuccessfulWithoutReason()
    {
        var accountantId = await _factory.CreateStaffAsync("rule.acct", AppRoles.Accountant, "Rules#Password123", "Rules");
        var employee = await _factory.CreateEmployeeAsync("rules.employee");
        var requestId = await SubmitAsync(employee);

        await using var context = TestDatabase.CreateContext();
        var request = await context.PaymentRequests.SingleAsync(item => item.Id == requestId);
        request.Status = PaymentRequestStatus.Approved;
        var outcome = new TransferOutcome
        {
            PaymentRequestId = requestId,
            Status = TransferStatus.Unsuccessful,
            Method = TransferMethod.Cash,
            PerformedByUserId = accountantId,
            PerformedByFullNameSnapshot = "Rules",
            PerformedByRoleSnapshot = AppRoles.Accountant,
            TransactionOccurredAtUtc = DateTimeOffset.UtcNow,
            TransactionUtcOffsetMinutes = 0,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            FailureReason = null
        };
        context.TransferOutcomes.Add(outcome);

        var missingReason = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(DatabaseFailureKind.ConstraintViolation, DatabaseErrorClassifier.Classify(missingReason).Kind);

        outcome.FailureReason = "Bank rejected";
        await context.SaveChangesAsync();

        outcome.IsClosed = true;
        outcome.ClosedAtUtc = DateTimeOffset.UtcNow;
        outcome.ClosureType = TransferClosureType.Automatic;
        outcome.ClosureReason = "   ";
        var missingClosureReason = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var failure = DatabaseErrorClassifier.Classify(missingClosureReason);
        Assert.Equal(DatabaseFailureKind.ConstraintViolation, failure.Kind);
        Assert.Equal("CK_TransferOutcomes_ClosureState", failure.ConstraintName);
    }

    private async Task<int> SubmitAsync((int Id, string Username, string AccessKey) employee)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentRequestSubmissionService>();
        var result = await service.SubmitAsync(
            new EmployeeProfile(employee.Id, "Employee", employee.Username, null, null, null, null),
            [new PaymentRequestLineInput("Row", 1_000, false, null)], [], "Payee", "Reason", null,
            PaymentRequestSubmissionRules.CreateSubmissionKey());
        return result.RequestId;
    }

    private async Task<ApprovalDecisionResult> DecideAsync(int requestId, uint version, int adminId, ApprovalDecisionType decision)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IApprovalDecisionService>();
        return await service.DecideAsync(requestId, version, adminId, "Decider", decision, null);
    }

    private async Task<StaffOperationResult> DeactivateAsync(int targetId, AuditActor actor)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStaffAccountService>();
        return await service.DeactivateAsync(targetId, actor);
    }
}
