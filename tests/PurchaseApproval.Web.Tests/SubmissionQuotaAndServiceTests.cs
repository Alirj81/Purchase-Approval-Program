// Exercises the real submission service over EF InMemory: idempotent replay, the configured
// 10-per-UTC-day quota, and the rule that invalid attempts and duplicates consume no quota.
// Atomic PostgreSQL behaviour (row locking) is covered by the integration test project.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class SubmissionQuotaAndServiceTests
{
    private static readonly EmployeeProfile Employee = new(
        42, "Current Employee", "employee42", null, "Operations", null, null);

    private static readonly ApplicationUserProfile Administrator = new(
        9, "Approval Admin", "approval.admin", null, "Management", null, null);

    [Fact]
    public async Task SameSubmissionKey_ReturnsTheOriginalRequestWithoutConsumingQuota()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var service = CreateService(dbContext, dailyQuota: 10);
        var key = PaymentRequestSubmissionRules.CreateSubmissionKey();

        var first = await service.SubmitAsync(Employee, Lines(), [], "Payee", "Reason", null, key);
        var replay = await service.SubmitAsync(Employee, Lines(), [], "Payee", "Reason", null, key);

        Assert.False(first.IsDuplicate);
        Assert.NotNull(first.TrackingToken);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(first.RequestId, replay.RequestId);
        Assert.Null(replay.TrackingToken);
        Assert.Equal(1, await dbContext.PaymentRequests.CountAsync());
        Assert.Equal(1, (await dbContext.SubmissionQuotaDays.SingleAsync()).CreatedCount);
    }

    [Fact]
    public async Task DailyQuota_AppliesPerAccountPerUtcDay_ForBothRequesterTypes()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 19, 23, 30, 0, TimeSpan.Zero));
        var service = CreateService(dbContext, dailyQuota: 2, clock);

        await service.SubmitAsync(Employee, Lines(), [], "Payee", "Reason", null, NewKey());
        await service.SubmitAsync(Employee, Lines(), [], "Payee", "Reason", null, NewKey());
        var exceeded = await Assert.ThrowsAsync<SubmissionQuotaExceededException>(() =>
            service.SubmitAsync(Employee, Lines(), [], "Payee", "Reason", null, NewKey()));

        // Another account is unaffected, and the administrator has their own quota.
        await service.SubmitAsync(Administrator, Lines(), [], "Payee", "Reason", null, NewKey());
        await service.SubmitAsync(Administrator, Lines(), [], "Payee", "Reason", null, NewKey());
        await Assert.ThrowsAsync<SubmissionQuotaExceededException>(() =>
            service.SubmitAsync(Administrator, Lines(), [], "Payee", "Reason", null, NewKey()));

        // After UTC midnight the counter starts again (a browser timezone cannot do this).
        clock.Value = new DateTimeOffset(2026, 10, 20, 0, 0, 1, TimeSpan.Zero);
        await service.SubmitAsync(Employee, Lines(), [], "Payee", "Reason", null, NewKey());

        Assert.Equal(2, exceeded.DailyLimit);
        Assert.Contains(exceeded.Errors, error => error.Contains("UTC day", StringComparison.Ordinal));
        Assert.Equal(5, await dbContext.PaymentRequests.CountAsync());
        Assert.Equal(3, await dbContext.SubmissionQuotaDays.CountAsync());
    }

    [Fact]
    public async Task InvalidSubmission_ConsumesNoQuotaAndCreatesNothing()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var service = CreateService(dbContext, dailyQuota: 1);

        await Assert.ThrowsAsync<PaymentRequestValidationException>(() =>
            service.SubmitAsync(Employee, [], [], "Payee", "Reason", null, NewKey()));
        var valid = await service.SubmitAsync(Employee, Lines(), [], "Payee", "Reason", null, NewKey());

        Assert.False(valid.IsDuplicate);
        Assert.Equal(1, await dbContext.PaymentRequests.CountAsync());
    }

    [Fact]
    public async Task AboveCeilingTotal_TellsTheRequesterToContactTheCompany()
    {
        await using var dbContext = TestSupport.CreateInMemoryContext();
        var service = CreateService(dbContext, dailyQuota: 10);

        var exception = await Assert.ThrowsAsync<PaymentRequestValidationException>(() =>
            service.SubmitAsync(
                Employee,
                [new PaymentRequestLineInput("Too much", PaymentRequestSubmissionRules.MaximumTotalRials + 1, false, null)],
                [], "Payee", "Reason", null, NewKey()));

        Assert.Contains(exception.Errors, error => error.Contains("contact the company", StringComparison.Ordinal));
        Assert.Equal(0, await dbContext.PaymentRequests.CountAsync());
    }

    [Fact]
    public void AttachmentPolicy_RejectsTypesOutsideTheConfiguredAllowlist()
    {
        var pdfOnly = new AttachmentPolicy(5, 5L * 1024 * 1024, new HashSet<string>(["application/pdf"]));

        var exception = Assert.Throws<PaymentRequestValidationException>(() =>
            PaymentRequestSubmissionRules.CreateRequest(
                Employee,
                Lines(),
                [new PaymentRequestAttachmentInput("photo.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1])],
                "Payee", "Reason", null, "PR-TEST", new string('A', 64), DateTimeOffset.UtcNow, pdfOnly));

        Assert.Contains(exception.Errors, error => error.Contains("PDF, JPEG, or PNG", StringComparison.Ordinal));
    }

    private static PaymentRequestSubmissionService CreateService(
        PurchaseApproval.Data.ApplicationDbContext dbContext,
        int dailyQuota,
        TimeProvider? clock = null)
        => new(
            dbContext,
            clock ?? new FixedClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)),
            new SubmissionQuotaService(dbContext),
            TestSupport.Limits(options => options.DailySubmissionQuota = dailyQuota));

    private static List<PaymentRequestLineInput> Lines()
        => [new PaymentRequestLineInput("Paper", 250_000, true, null)];

    private static string NewKey() => PaymentRequestSubmissionRules.CreateSubmissionKey();
}
