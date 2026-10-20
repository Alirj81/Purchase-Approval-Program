// Verifies the hosted closure worker (TransferClosureHostedService), not just the processor:
// it must run on its own when the host starts, catch up on items that became due while no
// host was running, leave items under the 24/48-hour thresholds and approved-but-unrecorded
// items open, and never close an item twice when two hosts overlap. Each WebApplicationFactory
// below is one application start; disposing it is the downtime between two IIS recycles. IIS
// itself (Always Running, preload) is verified separately on a Windows Server.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ClosureWorkerLifecycleTests : IAsyncLifetime
{
    private static readonly TimeSpan WorkerWait = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        if (TestDatabase.IsAvailable)
        {
            await TestDatabase.ResetAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PostgresFact]
    public async Task HostedWorker_ClosesOverdueItemsAtStartup_CatchesUpAfterDowntime_AndNeverClosesTwice()
    {
        // Seed accounts and six pending requests through a short-lived host (roles and services
        // exist only inside a host); its own worker finds nothing to close and is gone before the
        // outcomes below are written.
        int accountantId;
        var requestIds = new List<int>();
        using (var seedHost = new TestWebApplicationFactory())
        {
            accountantId = await seedHost.CreateStaffAsync("worker.acct", AppRoles.Accountant, "Worker#Password123", "Worker Accountant");
            var employee = await seedHost.CreateEmployeeAsync("worker.employee");
            for (var i = 0; i < 6; i++)
            {
                requestIds.Add(await SubmitAsync(seedHost, employee));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var overdueTransferred = await RecordOutcomeAsync(requestIds[0], accountantId, TransferStatus.Transferred, now.AddHours(-25), null);
        var overdueUnsuccessful = await RecordOutcomeAsync(requestIds[1], accountantId, TransferStatus.Unsuccessful, now.AddHours(-49), "Card declined");
        var freshTransferred = await RecordOutcomeAsync(requestIds[2], accountantId, TransferStatus.Transferred, now.AddHours(-23), null);
        var freshUnsuccessful = await RecordOutcomeAsync(requestIds[3], accountantId, TransferStatus.Unsuccessful, now.AddHours(-47), "Bank offline");
        var awaitingFirstOutcome = await RecordOutcomeAsync(requestIds[4], accountantId, TransferStatus.AwaitingTransfer, null, null);

        // First "pool start": nobody visits the site; the worker alone must close the two overdue items.
        using (var firstStart = new TestWebApplicationFactory())
        {
            _ = firstStart.Services;
            Assert.True(
                await WaitUntilAsync(async () => await IsClosedAsync(overdueTransferred) && await IsClosedAsync(overdueUnsuccessful)),
                "The hosted worker did not close the overdue items after start-up.");
        }

        await using (var verify = TestDatabase.CreateContext())
        {
            var fresh = await verify.TransferOutcomes
                .Where(item => item.PaymentRequestId == freshTransferred || item.PaymentRequestId == freshUnsuccessful)
                .ToListAsync();
            Assert.Equal(2, fresh.Count);
            Assert.All(fresh, item => Assert.False(item.IsClosed));
            var awaiting = await verify.TransferOutcomes.SingleAsync(item => item.PaymentRequestId == awaitingFirstOutcome);
            Assert.False(awaiting.IsClosed);
            Assert.Equal(TransferStatus.AwaitingTransfer, awaiting.Status);
        }

        // Downtime: an item becomes due while no host is running. Then two hosts start at once
        // (an overlapping recycle); exactly one of them may close it.
        var dueDuringDowntime = await RecordOutcomeAsync(requestIds[5], accountantId, TransferStatus.Transferred, now.AddHours(-30), null);
        using (var secondStart = new TestWebApplicationFactory())
        using (var overlappingStart = new TestWebApplicationFactory())
        {
            _ = secondStart.Services;
            _ = overlappingStart.Services;
            Assert.True(await WaitUntilAsync(() => IsClosedAsync(dueDuringDowntime)),
                "The item that became due during downtime was not closed after the restart.");
            // Give the other host's start-up run time to finish before checking for duplicates.
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        await using (var verify = TestDatabase.CreateContext())
        {
            foreach (var (requestId, expectedStatus, expectedReason, expectedAction, failureReason) in new[]
                     {
                         (overdueTransferred, TransferStatus.Transferred, TransferExecutionRules.AutomaticTransferredClosureReason, TransferAuditAction.TransferredClosedAutomatically, (string?)null),
                         (overdueUnsuccessful, TransferStatus.Unsuccessful, TransferExecutionRules.AutomaticUnsuccessfulClosureReason, TransferAuditAction.UnsuccessfulClosedAutomatically, "Card declined"),
                         (dueDuringDowntime, TransferStatus.Transferred, TransferExecutionRules.AutomaticTransferredClosureReason, TransferAuditAction.TransferredClosedAutomatically, null)
                     })
            {
                var item = await verify.TransferOutcomes.Include(outcome => outcome.AuditEntries)
                    .SingleAsync(outcome => outcome.PaymentRequestId == requestId);
                Assert.True(item.IsClosed);
                Assert.Equal(expectedStatus, item.Status);
                Assert.Equal(TransferClosureType.Automatic, item.ClosureType);
                Assert.Equal(expectedReason, item.ClosureReason);
                Assert.Null(item.ClosedByUserId);
                Assert.NotNull(item.ClosedAtUtc);
                Assert.True(item.ClosedAtUtc >= TransferExecutionRules.GetAutomaticClosureDueAt(item));
                // Outcome, reason and executor snapshot survive closure untouched.
                Assert.Equal(failureReason, item.FailureReason);
                Assert.Equal(accountantId, item.PerformedByUserId);
                Assert.Equal("Worker Accountant", item.PerformedByFullNameSnapshot);
                // Exactly one automatic-closure audit row, by the system actor.
                var closure = Assert.Single(item.AuditEntries, audit => audit.Action == expectedAction);
                Assert.Null(closure.ActorUserId);
                Assert.Equal("System", closure.ActorRoleSnapshot);
                Assert.Equal(expectedReason, closure.ClosureReason);
            }

            Assert.Equal(6, await verify.TransferOutcomes.CountAsync());
            Assert.Equal(3, await verify.TransferOutcomes.CountAsync(item => item.IsClosed));
        }
    }

    private static async Task<int> SubmitAsync(TestWebApplicationFactory host, (int Id, string Username, string AccessKey) employee)
    {
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentRequestSubmissionService>();
        var result = await service.SubmitAsync(
            new EmployeeProfile(employee.Id, "Worker Employee", employee.Username, null, null, null, null),
            [new PaymentRequestLineInput("Row", 1_000, false, null)], [], "Payee", "Reason", null,
            PaymentRequestSubmissionRules.CreateSubmissionKey());
        return result.RequestId;
    }

    /// <summary>Writes synthetic approved-and-recorded state directly; RecordedAtUtc is the server recording time the timers use.</summary>
    private static async Task<int> RecordOutcomeAsync(
        int requestId,
        int accountantId,
        TransferStatus status,
        DateTimeOffset? recordedAtUtc,
        string? failureReason)
    {
        await using var context = TestDatabase.CreateContext();
        var request = await context.PaymentRequests.SingleAsync(item => item.Id == requestId);
        request.Status = PaymentRequestStatus.Approved;
        context.TransferOutcomes.Add(status == TransferStatus.AwaitingTransfer
            ? new TransferOutcome { PaymentRequestId = requestId, Status = TransferStatus.AwaitingTransfer, IsClosed = false }
            : new TransferOutcome
            {
                PaymentRequestId = requestId,
                Status = status,
                Method = TransferMethod.Cash,
                PerformedByUserId = accountantId,
                PerformedByFullNameSnapshot = "Worker Accountant",
                PerformedByRoleSnapshot = AppRoles.Accountant,
                TransactionOccurredAtUtc = recordedAtUtc!.Value.AddMinutes(-5),
                TransactionUtcOffsetMinutes = 0,
                RecordedAtUtc = recordedAtUtc,
                FailureReason = failureReason,
                IsClosed = false
            });
        await context.SaveChangesAsync();
        return requestId;
    }

    private static async Task<bool> IsClosedAsync(int requestId)
    {
        await using var context = TestDatabase.CreateContext();
        return await context.TransferOutcomes.AsNoTracking()
            .AnyAsync(item => item.PaymentRequestId == requestId && item.IsClosed);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + WorkerWait;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(250);
        }

        return await condition();
    }
}
