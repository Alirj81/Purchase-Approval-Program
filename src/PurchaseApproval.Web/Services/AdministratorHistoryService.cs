// Builds the read-only administrator history across request, decision, execution, and closure states.
// Date filtering always names its timestamp field and converts explicit local-zone boundaries to UTC.
// Results are paged with a stable order (timestamp, then reference) so a page never shifts
// between two reads of the same data. The page model supplies the default three-month window.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public enum HistoryTimestampField
{
    Submission = 1,
    Decision = 2,
    ExecutionRecording = 3,
    Closure = 4
}

public enum HistoryClosureFilter
{
    All = 0,
    Open = 1,
    Closed = 2
}

public enum HistorySortDirection
{
    NewestFirst = 1,
    OldestFirst = 2
}

public sealed record AdministratorHistoryFilter(
    string? Search,
    PaymentRequestStatus? ApprovalStatus,
    TransferStatus? ExecutionOutcome,
    HistoryClosureFilter Closure,
    HistoryTimestampField TimestampField,
    HistorySortDirection SortDirection,
    ReportPeriod? Period,
    TimeZoneInfo? TimeZone,
    RequesterType? RequesterType = null,
    int? RequesterId = null,
    bool? SelfApprovedOnly = null,
    int Page = 1,
    int PageSize = 50,
    // Default "recent history" window on the submission timestamp, applied independently of
    // the explicit Period/TimestampField filter so sorting by another field keeps the window.
    ReportDateWindow? SubmissionWindow = null);

public sealed record AdministratorHistoryRow(
    int RequestId,
    string ReferenceNumber,
    RequesterType RequesterType,
    int RequesterId,
    string RequesterName,
    string RequesterUsername,
    string PayeeName,
    long TotalAmountRials,
    PaymentRequestStatus ApprovalStatus,
    DateTimeOffset SubmittedAtUtc,
    ApprovalDecisionType? DecisionType,
    DateTimeOffset? DecidedAtUtc,
    string? DecidedByName,
    bool IsSelfApproval,
    TransferStatus? ExecutionOutcome,
    DateTimeOffset? TransactionOccurredAtUtc,
    short? TransactionUtcOffsetMinutes,
    DateTimeOffset? ExecutionRecordedAtUtc,
    string? ExecutorName,
    string? FailureReason,
    string? ExternalReference,
    bool? IsClosed,
    TransferClosureType? ClosureType,
    DateTimeOffset? ClosedAtUtc,
    string? ClosedByName,
    string? ClosureReason);

public sealed record AdministratorHistoryPage(
    IReadOnlyList<AdministratorHistoryRow> Rows,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public interface IAdministratorHistoryService
{
    Task<AdministratorHistoryPage> SearchAsync(
        AdministratorHistoryFilter filter,
        CancellationToken cancellationToken = default);
}

public sealed class AdministratorHistoryService(
    ApplicationDbContext dbContext) : IAdministratorHistoryService
{
    public const int MaximumPageSize = 200;

    public async Task<AdministratorHistoryPage> SearchAsync(
        AdministratorHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Period is not null && filter.TimeZone is null)
        {
            throw new ArgumentException(
                "A timezone is required when filtering history by local date.",
                nameof(filter));
        }

        var query = dbContext.PaymentRequests.AsNoTracking();
        var search = string.IsNullOrWhiteSpace(filter.Search)
            ? null
            : filter.Search.Trim().ToUpper();
        if (search is not null)
        {
            query = query.Where(item =>
                item.ReferenceNumber.ToUpper().Contains(search)
                || item.RequesterFullNameSnapshot.ToUpper().Contains(search)
                || item.RequesterUsernameSnapshot.ToUpper().Contains(search)
                || item.PayeeName.ToUpper().Contains(search));
        }

        if (filter.RequesterType is { } requesterType)
        {
            query = query.Where(item => item.RequesterType == requesterType);
        }

        if (filter.RequesterId is { } requesterId)
        {
            query = filter.RequesterType == Models.RequesterType.Administrator
                ? query.Where(item => item.ApplicationUserRequesterId == requesterId)
                : query.Where(item => item.EmployeeRequesterId == requesterId);
        }

        if (filter.ApprovalStatus is { } approvalStatus)
        {
            query = query.Where(item => item.Status == approvalStatus);
        }

        if (filter.SelfApprovedOnly == true)
        {
            query = query.Where(item => item.Decision != null && item.Decision.IsSelfApproval);
        }

        if (filter.ExecutionOutcome is { } executionOutcome)
        {
            query = executionOutcome == TransferStatus.AwaitingTransfer
                ? query.Where(item => item.Status == PaymentRequestStatus.Approved
                    && (item.TransferOutcome == null
                        || item.TransferOutcome.Status == TransferStatus.AwaitingTransfer))
                : query.Where(item => item.TransferOutcome != null
                    && item.TransferOutcome.Status == executionOutcome);
        }

        query = filter.Closure switch
        {
            HistoryClosureFilter.Open => query.Where(item =>
                item.Status == PaymentRequestStatus.Approved
                && (item.TransferOutcome == null || !item.TransferOutcome.IsClosed)),
            HistoryClosureFilter.Closed => query.Where(item =>
                item.TransferOutcome != null && item.TransferOutcome.IsClosed),
            _ => query
        };

        if (filter.SubmissionWindow is { } submissionWindow)
        {
            query = query.Where(item =>
                item.SubmittedAtUtc >= submissionWindow.StartUtc
                && item.SubmittedAtUtc < submissionWindow.EndExclusiveUtc);
        }

        if (filter.Period is { } period)
        {
            var window = ReportDateWindow.Create(period, filter.TimeZone!);
            query = filter.TimestampField switch
            {
                HistoryTimestampField.Submission => query.Where(item =>
                    item.SubmittedAtUtc >= window.StartUtc
                    && item.SubmittedAtUtc < window.EndExclusiveUtc),
                HistoryTimestampField.Decision => query.Where(item => item.Decision != null
                    && item.Decision.DecidedAtUtc >= window.StartUtc
                    && item.Decision.DecidedAtUtc < window.EndExclusiveUtc),
                HistoryTimestampField.ExecutionRecording => query.Where(item =>
                    item.TransferOutcome != null
                    && item.TransferOutcome.RecordedAtUtc >= window.StartUtc
                    && item.TransferOutcome.RecordedAtUtc < window.EndExclusiveUtc),
                HistoryTimestampField.Closure => query.Where(item =>
                    item.TransferOutcome != null
                    && item.TransferOutcome.ClosedAtUtc >= window.StartUtc
                    && item.TransferOutcome.ClosedAtUtc < window.EndExclusiveUtc),
                _ => throw new ArgumentOutOfRangeException(nameof(filter))
            };
        }

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);
        var totalCount = await query.CountAsync(cancellationToken);

        query = ApplyOrdering(query, filter.TimestampField, filter.SortDirection);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new AdministratorHistoryRow(
                item.Id,
                item.ReferenceNumber,
                item.RequesterType,
                item.EmployeeRequesterId ?? item.ApplicationUserRequesterId ?? 0,
                item.RequesterFullNameSnapshot,
                item.RequesterUsernameSnapshot,
                item.PayeeName,
                item.TotalAmountRials,
                item.Status,
                item.SubmittedAtUtc,
                item.Decision == null ? null : item.Decision.DecisionType,
                item.Decision == null ? null : item.Decision.DecidedAtUtc,
                item.Decision == null ? null : item.Decision.DecidedByFullNameSnapshot,
                item.Decision != null && item.Decision.IsSelfApproval,
                item.Status != PaymentRequestStatus.Approved
                    ? null
                    : item.TransferOutcome == null
                        ? TransferStatus.AwaitingTransfer
                        : item.TransferOutcome.Status,
                item.TransferOutcome == null ? null : item.TransferOutcome.TransactionOccurredAtUtc,
                item.TransferOutcome == null ? null : item.TransferOutcome.TransactionUtcOffsetMinutes,
                item.TransferOutcome == null ? null : item.TransferOutcome.RecordedAtUtc,
                item.TransferOutcome == null ? null : item.TransferOutcome.PerformedByFullNameSnapshot,
                item.TransferOutcome == null ? null : item.TransferOutcome.FailureReason,
                item.TransferOutcome == null ? null : item.TransferOutcome.ExternalReference,
                item.Status != PaymentRequestStatus.Approved
                    ? null
                    : item.TransferOutcome == null
                        ? false
                        : item.TransferOutcome.IsClosed,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosureType,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosedAtUtc,
                item.TransferOutcome == null
                    ? null
                    : item.TransferOutcome.ClosedByFullNameSnapshot,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosureReason))
            .ToListAsync(cancellationToken);

        return new AdministratorHistoryPage(rows, totalCount, page, pageSize);
    }

    private static IQueryable<PaymentRequest> ApplyOrdering(
        IQueryable<PaymentRequest> query,
        HistoryTimestampField timestampField,
        HistorySortDirection direction)
    {
        var newestFirst = direction == HistorySortDirection.NewestFirst;
        return timestampField switch
        {
            HistoryTimestampField.Submission => newestFirst
                ? query.OrderByDescending(item => item.SubmittedAtUtc)
                    .ThenBy(item => item.ReferenceNumber)
                : query.OrderBy(item => item.SubmittedAtUtc)
                    .ThenBy(item => item.ReferenceNumber),
            HistoryTimestampField.Decision => newestFirst
                ? query.OrderBy(item => item.Decision == null)
                    .ThenByDescending(item => item.Decision!.DecidedAtUtc)
                    .ThenBy(item => item.ReferenceNumber)
                : query.OrderBy(item => item.Decision == null)
                    .ThenBy(item => item.Decision!.DecidedAtUtc)
                    .ThenBy(item => item.ReferenceNumber),
            HistoryTimestampField.ExecutionRecording => newestFirst
                ? query.OrderBy(item => item.TransferOutcome == null
                        || item.TransferOutcome.RecordedAtUtc == null)
                    .ThenByDescending(item => item.TransferOutcome!.RecordedAtUtc)
                    .ThenBy(item => item.ReferenceNumber)
                : query.OrderBy(item => item.TransferOutcome == null
                        || item.TransferOutcome.RecordedAtUtc == null)
                    .ThenBy(item => item.TransferOutcome!.RecordedAtUtc)
                    .ThenBy(item => item.ReferenceNumber),
            HistoryTimestampField.Closure => newestFirst
                ? query.OrderBy(item => item.TransferOutcome == null
                        || item.TransferOutcome.ClosedAtUtc == null)
                    .ThenByDescending(item => item.TransferOutcome!.ClosedAtUtc)
                    .ThenBy(item => item.ReferenceNumber)
                : query.OrderBy(item => item.TransferOutcome == null
                        || item.TransferOutcome.ClosedAtUtc == null)
                    .ThenBy(item => item.TransferOutcome!.ClosedAtUtc)
                    .ThenBy(item => item.ReferenceNumber),
            _ => throw new ArgumentOutOfRangeException(nameof(timestampField))
        };
    }
}
