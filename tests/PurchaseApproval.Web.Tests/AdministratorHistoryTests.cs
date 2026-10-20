// Verifies administrator history coverage, filters, chronology, paging and explicit local-date boundaries.
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Services;
using AdminHistoryPage = PurchaseApproval.Web.Pages.Admin.History.IndexModel;

namespace PurchaseApproval.Web.Tests;

public sealed class AdministratorHistoryTests
{
    [Fact]
    public void Dashboard_RequiresAdministratorDecisionPolicy()
    {
        var authorization = typeof(AdminHistoryPage).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal(PurchaseApproval.Security.AppPolicies.CanDecideRequests, authorization.Policy);
    }

    [Fact]
    public async Task Dashboard_CoversAllStatusesRequesterTypesAndExecutionClosureStates()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(
            CreateRequest(1, "PR-PENDING", RequesterType.Employee, PaymentRequestStatus.Pending),
            CreateRequest(2, "PR-REJECTED", RequesterType.Administrator, PaymentRequestStatus.Rejected),
            CreateExecutedRequest(3, "PR-CLOSED", TransferStatus.Transferred, isClosed: true),
            CreateExecutedRequest(4, "PR-OPEN-FAIL", TransferStatus.Unsuccessful, isClosed: false));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = new AdministratorHistoryService(dbContext);

        var all = await service.SearchAsync(Filter());
        var closed = await service.SearchAsync(Filter() with
        {
            Closure = HistoryClosureFilter.Closed
        });
        var unsuccessful = await service.SearchAsync(Filter() with
        {
            ExecutionOutcome = TransferStatus.Unsuccessful,
            Closure = HistoryClosureFilter.Open,
            Search = "open-fail"
        });

        Assert.Equal(4, all.TotalCount);
        Assert.Equal(4, all.Rows.Count);
        Assert.Contains(all.Rows, row => row.RequesterType == RequesterType.Employee);
        Assert.Contains(all.Rows, row => row.RequesterType == RequesterType.Administrator);
        Assert.Contains(all.Rows, row => row.ApprovalStatus == PaymentRequestStatus.Pending);
        Assert.Contains(all.Rows, row => row.ApprovalStatus == PaymentRequestStatus.Rejected);
        var closedRow = Assert.Single(closed.Rows);
        Assert.Equal("PR-CLOSED", closedRow.ReferenceNumber);
        Assert.Equal("Automatic closure reason", closedRow.ClosureReason);
        var failed = Assert.Single(unsuccessful.Rows);
        Assert.Equal("Required failure reason", failed.FailureReason);
        Assert.Equal("EXT-004", failed.ExternalReference);
        Assert.False(failed.IsClosed);
        Assert.False(dbContext.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task PerPersonSelfApprovedAndPagingFilters_NarrowTheResult()
    {
        await using var dbContext = CreateDbContext();
        var selfApproved = CreateRequest(2, "PR-SELF", RequesterType.Administrator, PaymentRequestStatus.Approved);
        selfApproved.Decision = new ApprovalDecision
        {
            Id = 2,
            PaymentRequestId = 2,
            PaymentRequest = selfApproved,
            DecisionType = ApprovalDecisionType.Approved,
            DecidedByUserId = 202,
            DecidedByFullNameSnapshot = "Admin Two",
            IsSelfApproval = true,
            DecidedAtUtc = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero)
        };
        dbContext.AddRange(
            CreateRequest(1, "PR-ONE", RequesterType.Employee, PaymentRequestStatus.Pending),
            selfApproved,
            CreateRequest(3, "PR-THREE", RequesterType.Employee, PaymentRequestStatus.Pending));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = new AdministratorHistoryService(dbContext);

        var perPerson = await service.SearchAsync(Filter() with
        {
            RequesterType = RequesterType.Employee,
            RequesterId = 103
        });
        var selfOnly = await service.SearchAsync(Filter() with { SelfApprovedOnly = true });
        var secondPage = await service.SearchAsync(Filter() with { Page = 2, PageSize = 2 });

        Assert.Equal("PR-THREE", Assert.Single(perPerson.Rows).ReferenceNumber);
        var self = Assert.Single(selfOnly.Rows);
        Assert.True(self.IsSelfApproval);
        Assert.Equal("Admin Two", self.DecidedByName);
        Assert.Equal(3, secondPage.TotalCount);
        Assert.Equal(2, secondPage.TotalPages);
        Assert.Single(secondPage.Rows);
    }

    [Fact]
    public async Task ClosureDateFilter_UsesInclusiveLocalDatesAndSelectedChronology()
    {
        await using var dbContext = CreateDbContext();
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "History +03:30",
            TimeSpan.FromMinutes(210),
            "History +03:30",
            "History +03:30");
        var period = new ReportPeriod(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19));
        var window = ReportDateWindow.Create(period, timeZone);

        var atStart = CreateExecutedRequest(1, "PR-START", TransferStatus.Transferred, true);
        atStart.TransferOutcome!.ClosedAtUtc = window.StartUtc;
        var atEnd = CreateExecutedRequest(2, "PR-END", TransferStatus.Transferred, true);
        atEnd.TransferOutcome!.ClosedAtUtc = window.EndExclusiveUtc.AddTicks(-1);
        var outside = CreateExecutedRequest(3, "PR-OUTSIDE", TransferStatus.Transferred, true);
        outside.TransferOutcome!.ClosedAtUtc = window.EndExclusiveUtc;
        dbContext.AddRange(atStart, atEnd, outside);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var rows = await new AdministratorHistoryService(dbContext).SearchAsync(Filter() with
        {
            TimestampField = HistoryTimestampField.Closure,
            SortDirection = HistorySortDirection.OldestFirst,
            Period = period,
            TimeZone = timeZone
        });

        Assert.Equal(["PR-START", "PR-END"], rows.Rows.Select(row => row.ReferenceNumber).ToArray());
    }

    [Fact]
    public async Task DefaultSubmissionWindow_LimitsRowsIndependentlyOfTheSortField()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AddRange(
            CreateRequest(1, "PR-OLD", RequesterType.Employee, PaymentRequestStatus.Pending),
            CreateRequest(2, "PR-NEW", RequesterType.Employee, PaymentRequestStatus.Pending));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        // Submissions are on 2026-10-18 (id 1) and 2026-10-19 (id 2); window covers only the 19th.
        var window = ReportDateWindow.Create(
            new ReportPeriod(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19)), TimeZoneInfo.Utc);
        var rows = await new AdministratorHistoryService(dbContext).SearchAsync(Filter() with
        {
            TimestampField = HistoryTimestampField.Decision,
            SubmissionWindow = window
        });

        Assert.Equal("PR-NEW", Assert.Single(rows.Rows).ReferenceNumber);
    }

    private static AdministratorHistoryFilter Filter() => new(
        null,
        null,
        null,
        HistoryClosureFilter.All,
        HistoryTimestampField.Submission,
        HistorySortDirection.NewestFirst,
        null,
        null);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static PaymentRequest CreateExecutedRequest(
        int id,
        string reference,
        TransferStatus status,
        bool isClosed)
    {
        var request = CreateRequest(
            id,
            reference,
            id % 2 == 0 ? RequesterType.Administrator : RequesterType.Employee,
            PaymentRequestStatus.Approved);
        request.TransferOutcome = new TransferOutcome
        {
            Id = id,
            PaymentRequestId = id,
            PaymentRequest = request,
            Status = status,
            Method = TransferMethod.Cash,
            PerformedByUserId = 20,
            PerformedByFullNameSnapshot = "Executor",
            PerformedByRoleSnapshot = "Accountant",
            TransactionOccurredAtUtc = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero),
            TransactionUtcOffsetMinutes = 120,
            RecordedAtUtc = new DateTimeOffset(2026, 10, 18, 9, 5, 0, TimeSpan.Zero),
            FailureReason = status == TransferStatus.Unsuccessful ? "Required failure reason" : null,
            ExternalReference = $"EXT-{id:000}",
            IsClosed = isClosed,
            ClosureType = isClosed ? TransferClosureType.Automatic : null,
            ClosureReason = isClosed ? "Automatic closure reason" : null,
            ClosedAtUtc = isClosed
                ? new DateTimeOffset(2026, 10, 19, 9, 5, 0, TimeSpan.Zero)
                : null
        };
        return request;
    }

    private static PaymentRequest CreateRequest(
        int id,
        string reference,
        RequesterType requesterType,
        PaymentRequestStatus status) => new()
    {
        Id = id,
        ReferenceNumber = reference,
        RequesterType = requesterType,
        EmployeeRequesterId = requesterType == RequesterType.Employee ? 100 + id : null,
        ApplicationUserRequesterId = requesterType == RequesterType.Administrator ? 200 + id : null,
        RequesterFullNameSnapshot = requesterType == RequesterType.Employee
            ? "Employee Requester"
            : "Administrator Requester",
        RequesterUsernameSnapshot = $"requester-{id}",
        Justification = "Purpose",
        PayeeName = "Payee",
        TotalAmountRials = 1_000,
        Status = status,
        SubmittedAtUtc = new DateTimeOffset(2026, 10, 17 + id, 8, 0, 0, TimeSpan.Zero),
        TrackingTokenHash = new string('A', 64)
    };
}
