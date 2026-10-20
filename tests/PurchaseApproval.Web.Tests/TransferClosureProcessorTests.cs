// Verifies timer boundaries and startup-style recovery using a fresh DbContext over persisted rows.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class TransferClosureProcessorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshProcessor_ClosesBothOverdueTimersAndLeavesFutureItemOpen()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.AddRange(
                CreateRequest(1, TransferStatus.Transferred, Now.AddHours(-24), null),
                CreateRequest(2, TransferStatus.Unsuccessful, Now.AddHours(-48), "Attempt failed"),
                CreateRequest(3, TransferStatus.Transferred, Now.AddHours(-24).AddTicks(1), null));
            await seed.SaveChangesAsync();
        }

        await using (var processing = new ApplicationDbContext(options))
        {
            var processor = new TransferClosureProcessor(
                processing,
                new FixedTimeProvider(Now));

            Assert.Equal(2, await processor.CloseOverdueAsync());
        }

        await using var verification = new ApplicationDbContext(options);
        var transfers = await verification.TransferOutcomes
            .AsNoTracking()
            .Include(item => item.AuditEntries)
            .OrderBy(item => item.Id)
            .ToListAsync();

        Assert.True(transfers[0].IsClosed);
        Assert.True(transfers[1].IsClosed);
        Assert.False(transfers[2].IsClosed);
        Assert.All(transfers.Take(2), transfer =>
        {
            Assert.Equal(TransferClosureType.Automatic, transfer.ClosureType);
            Assert.Equal(Now, transfer.ClosedAtUtc);
            Assert.Null(transfer.ClosedByUserId);
            var audit = Assert.Single(transfer.AuditEntries);
            Assert.Null(audit.ActorUserId);
            Assert.Equal("System", audit.ActorRoleSnapshot);
            Assert.Equal(transfer.Status, audit.StatusAfterAction);
        });
    }

    private static PaymentRequest CreateRequest(
        int id,
        TransferStatus status,
        DateTimeOffset recordedAtUtc,
        string? failureReason)
    {
        var request = new PaymentRequest
        {
            Id = id,
            ReferenceNumber = $"PR-CLOSE-{id}",
            RequesterType = RequesterType.Administrator,
            ApplicationUserRequesterId = 100 + id,
            RequesterFullNameSnapshot = "Requester",
            RequesterUsernameSnapshot = $"requester-{id}",
            Justification = "Purpose",
            PayeeName = "Payee",
            TotalAmountRials = 1_000,
            Status = PaymentRequestStatus.Approved,
            SubmittedAtUtc = recordedAtUtc.AddDays(-1),
            TrackingTokenHash = new string('A', 64)
        };
        request.TransferOutcome = new TransferOutcome
        {
            Id = id,
            PaymentRequestId = id,
            PaymentRequest = request,
            Status = status,
            Method = TransferMethod.Cash,
            PerformedByUserId = 8,
            PerformedByFullNameSnapshot = "Executor",
            PerformedByRoleSnapshot = "Accountant",
            TransactionOccurredAtUtc = recordedAtUtc.AddMinutes(-1),
            TransactionUtcOffsetMinutes = 0,
            RecordedAtUtc = recordedAtUtc,
            FailureReason = failureReason,
            IsClosed = false
        };
        return request;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
