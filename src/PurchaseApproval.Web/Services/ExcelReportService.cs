// Report queries use retained snapshots and never load attachment content or mutate workflow state.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public interface IExcelReportService
{
    Task<ExcelReportFile?> CreateIndividualAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task<ConsolidatedExcelReportResult> CreateConsolidatedAsync(
        ReportPeriod period,
        ResolvedReportingSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class ExcelReportService(
    ApplicationDbContext dbContext,
    IExcelWorkbookBuilder workbookBuilder,
    TimeProvider timeProvider) : IExcelReportService
{
    /// <summary>Creates a printable record only after a request has an approved or rejected decision.</summary>
    public async Task<ExcelReportFile?> CreateIndividualAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var header = await dbContext.PaymentRequests
            .AsNoTracking()
            .Where(item => item.Id == requestId
                && item.Status != PaymentRequestStatus.Pending
                && item.Decision != null)
            .Select(item => new
            {
                item.Id,
                item.ReferenceNumber,
                item.RequesterType,
                item.RequesterFullNameSnapshot,
                item.RequesterUsernameSnapshot,
                item.RequesterPhoneNumberSnapshot,
                item.RequesterDepartmentSnapshot,
                item.RequesterBranchSnapshot,
                item.RequesterJobTitleSnapshot,
                item.Justification,
                item.PayeeName,
                item.RequestedAccountOrCardNumber,
                item.TotalAmountRials,
                item.Status,
                item.SubmittedAtUtc,
                Decision = new DecisionReportSnapshot(
                    item.Decision!.DecisionType,
                    item.Decision.DecidedByUserId,
                    item.Decision.DecidedByFullNameSnapshot,
                    item.Decision.RejectionReason,
                    item.Decision.DecidedAtUtc,
                    item.Decision.IsSelfApproval),
                Transfer = item.TransferOutcome == null
                    ? null
                    : new TransferReportSnapshot(
                        item.TransferOutcome.Status,
                        item.TransferOutcome.Method,
                        item.TransferOutcome.OtherMethodDescription,
                        item.TransferOutcome.DestinationAccountOrCardNumber,
                        item.TransferOutcome.BankName,
                        item.TransferOutcome.ChequeNumber,
                        item.TransferOutcome.ChequeDate,
                        item.TransferOutcome.ChequePayeeName,
                        item.TransferOutcome.PerformedByUserId,
                        item.TransferOutcome.PerformedByFullNameSnapshot,
                        item.TransferOutcome.PerformedByRoleSnapshot,
                        item.TransferOutcome.TransactionOccurredAtUtc,
                        item.TransferOutcome.TransactionUtcOffsetMinutes,
                        item.TransferOutcome.RecordedAtUtc,
                        item.TransferOutcome.FailureReason,
                        item.TransferOutcome.ExternalReference,
                        item.TransferOutcome.IsClosed,
                        item.TransferOutcome.ClosureType,
                        item.TransferOutcome.ClosedAtUtc,
                        item.TransferOutcome.ClosedByUserId,
                        item.TransferOutcome.ClosedByFullNameSnapshot,
                        item.TransferOutcome.ClosedByRoleSnapshot,
                        item.TransferOutcome.ClosureReason,
                        item.TransferOutcome.TransactionTimeZoneId)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var lines = await dbContext.PaymentRequestLines
            .AsNoTracking()
            .Where(item => item.PaymentRequestId == requestId)
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new RequestLineReportRow(
                item.DisplayOrder,
                item.Description,
                item.AmountRials,
                item.HasInvoice,
                item.Remarks))
            .ToListAsync(cancellationToken);

        // Count only: financial exports must never embed stored attachment bytes.
        var attachmentCount = await dbContext.PaymentRequestAttachments
            .AsNoTracking()
            .CountAsync(item => item.PaymentRequestId == requestId, cancellationToken);

        var report = new IndividualRequestReport(
            header.Id,
            header.ReferenceNumber,
            header.RequesterType,
            header.RequesterFullNameSnapshot,
            header.RequesterUsernameSnapshot,
            header.RequesterPhoneNumberSnapshot,
            header.RequesterDepartmentSnapshot,
            header.RequesterBranchSnapshot,
            header.RequesterJobTitleSnapshot,
            header.Justification,
            header.PayeeName,
            header.RequestedAccountOrCardNumber,
            header.TotalAmountRials,
            header.Status,
            header.SubmittedAtUtc,
            attachmentCount,
            header.Decision,
            header.Transfer,
            lines);

        var content = workbookBuilder.BuildIndividual(report, timeProvider.GetUtcNow());
        return new ExcelReportFile(content, $"{report.ReferenceNumber}-decision-record.xlsx");
    }

    /// <summary>
    /// Selects each approved request once inside the inclusive local-date period and builds one worksheet.
    /// </summary>
    public async Task<ConsolidatedExcelReportResult> CreateConsolidatedAsync(
        ReportPeriod period,
        ResolvedReportingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(settings);

        var window = ReportDateWindow.Create(period, settings.CompanyTimeZone);
        var query = dbContext.PaymentRequests
            .AsNoTracking()
            .Where(item => item.Status == PaymentRequestStatus.Approved
                && item.Decision != null
                && item.Decision.DecisionType == ApprovalDecisionType.Approved);

        query = settings.DateBasis == ConsolidatedReportDateBasis.ApprovalDecision
            ? query.Where(item => item.Decision!.DecidedAtUtc >= window.StartUtc
                && item.Decision.DecidedAtUtc < window.EndExclusiveUtc)
            : query.Where(item => item.SubmittedAtUtc >= window.StartUtc
                && item.SubmittedAtUtc < window.EndExclusiveUtc);

        query = settings.DateBasis == ConsolidatedReportDateBasis.ApprovalDecision
            ? query.OrderBy(item => item.Decision!.DecidedAtUtc)
                .ThenBy(item => item.ReferenceNumber)
            : query.OrderBy(item => item.SubmittedAtUtc)
                .ThenBy(item => item.ReferenceNumber);

        // Project from the request header so child rows and attachments cannot duplicate requests.
        var rows = await query
            .Select(item => new ConsolidatedRequestReportRow(
                item.Id,
                item.ReferenceNumber,
                item.RequesterType,
                item.RequesterFullNameSnapshot,
                item.RequesterUsernameSnapshot,
                item.RequesterPhoneNumberSnapshot,
                item.RequesterDepartmentSnapshot,
                item.RequesterBranchSnapshot,
                item.RequesterJobTitleSnapshot,
                item.Justification,
                item.PayeeName,
                item.RequestedAccountOrCardNumber,
                item.TotalAmountRials,
                item.SubmittedAtUtc,
                item.Decision!.DecidedByUserId,
                item.Decision.DecidedByFullNameSnapshot,
                item.Decision.DecidedAtUtc,
                item.TransferOutcome == null
                    ? null
                    : new TransferReportSnapshot(
                        item.TransferOutcome.Status,
                        item.TransferOutcome.Method,
                        item.TransferOutcome.OtherMethodDescription,
                        item.TransferOutcome.DestinationAccountOrCardNumber,
                        item.TransferOutcome.BankName,
                        item.TransferOutcome.ChequeNumber,
                        item.TransferOutcome.ChequeDate,
                        item.TransferOutcome.ChequePayeeName,
                        item.TransferOutcome.PerformedByUserId,
                        item.TransferOutcome.PerformedByFullNameSnapshot,
                        item.TransferOutcome.PerformedByRoleSnapshot,
                        item.TransferOutcome.TransactionOccurredAtUtc,
                        item.TransferOutcome.TransactionUtcOffsetMinutes,
                        item.TransferOutcome.RecordedAtUtc,
                        item.TransferOutcome.FailureReason,
                        item.TransferOutcome.ExternalReference,
                        item.TransferOutcome.IsClosed,
                        item.TransferOutcome.ClosureType,
                        item.TransferOutcome.ClosedAtUtc,
                        item.TransferOutcome.ClosedByUserId,
                        item.TransferOutcome.ClosedByFullNameSnapshot,
                        item.TransferOutcome.ClosedByRoleSnapshot,
                        item.TransferOutcome.ClosureReason,
                        item.TransferOutcome.TransactionTimeZoneId),
                item.Decision.IsSelfApproval))
            .ToListAsync(cancellationToken);

        var totalAmountRials = rows.Aggregate(
            0L,
            (total, row) => checked(total + row.TotalAmountRials));
        if (rows.Count == 0)
        {
            return new ConsolidatedExcelReportResult(null, 0, 0);
        }

        var generatedAtUtc = timeProvider.GetUtcNow();
        var content = workbookBuilder.BuildConsolidated(
            rows,
            period,
            settings,
            generatedAtUtc);
        var name = $"approved-requests-{period.From:yyyyMMdd}-{period.To:yyyyMMdd}.xlsx";
        return new ConsolidatedExcelReportResult(
            new ExcelReportFile(content, name),
            rows.Count,
            totalAmountRials);
    }
}
