// Authenticated detail view of one of the employee's own requests. Ownership is enforced in the
// query (EmployeeRequesterId must match the cookie identity), so a guessed ID returns 404.
// Shows the submitted amounts, the decision with its requester-facing explanation and the
// minimal payment/closure labels; execution destinations, methods and internal reasons are
// deliberately not selected from the database.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DetailsModel(ApplicationDbContext dbContext) : PageModel
{
    public RequestDetail? Detail { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!TryGetEmployeeId(out var employeeId))
        {
            return Forbid();
        }

        var header = await dbContext.PaymentRequests.AsNoTracking()
            .Where(item => item.Id == id && item.EmployeeRequesterId == employeeId)
            .Select(item => new RequestDetail(
                item.Id,
                item.ReferenceNumber,
                item.PayeeName,
                item.RequestedAccountOrCardNumber,
                item.Justification,
                item.TotalAmountRials,
                item.Status,
                item.SubmittedAtUtc,
                item.Decision == null ? null : item.Decision.DecisionType,
                item.Decision == null ? null : item.Decision.DecidedAtUtc,
                item.Decision == null ? null : item.Decision.RejectionReason,
                item.Status != PaymentRequestStatus.Approved
                    ? null
                    : item.TransferOutcome == null
                        ? TransferStatus.AwaitingTransfer
                        : item.TransferOutcome.Status,
                item.TransferOutcome != null && item.TransferOutcome.IsClosed,
                Array.Empty<LineRow>(),
                Array.Empty<AttachmentRow>()))
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        if (header is null)
        {
            return NotFound();
        }

        var lines = await dbContext.PaymentRequestLines.AsNoTracking()
            .Where(item => item.PaymentRequestId == id)
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new LineRow(item.Description, item.AmountRials, item.HasInvoice, item.Remarks))
            .ToListAsync(HttpContext.RequestAborted);
        var attachments = await dbContext.PaymentRequestAttachments.AsNoTracking()
            .Where(item => item.PaymentRequestId == id)
            .OrderBy(item => item.Id)
            .Select(item => new AttachmentRow(item.Id, item.OriginalFileName, item.FileSizeBytes))
            .ToListAsync(HttpContext.RequestAborted);

        Detail = header with { Lines = lines, Attachments = attachments };
        return Page();
    }

    /// <summary>Returns one of the employee's own attachments; any other request ID yields 404.</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(int id, int attachmentId)
    {
        if (!TryGetEmployeeId(out var employeeId))
        {
            return Forbid();
        }

        var attachment = await dbContext.PaymentRequestAttachments.AsNoTracking()
            .Where(item => item.Id == attachmentId
                && item.PaymentRequestId == id
                && item.PaymentRequest.EmployeeRequesterId == employeeId)
            .Select(item => new { item.Content, item.ContentType, item.OriginalFileName })
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        return attachment is null
            ? NotFound()
            : File(attachment.Content, attachment.ContentType, attachment.OriginalFileName);
    }

    private bool TryGetEmployeeId(out int employeeId)
    {
        var identity = User.Identities.FirstOrDefault(item => item.IsAuthenticated
            && item.AuthenticationType == EmployeeAuthenticationDefaults.AuthenticationScheme);
        return int.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out employeeId)
            && employeeId > 0;
    }

    public sealed record RequestDetail(
        int Id,
        string ReferenceNumber,
        string PayeeName,
        string? RequestedAccountOrCardNumber,
        string Justification,
        long TotalAmountRials,
        PaymentRequestStatus Status,
        DateTimeOffset SubmittedAtUtc,
        ApprovalDecisionType? DecisionType,
        DateTimeOffset? DecidedAtUtc,
        string? RejectionReason,
        TransferStatus? Payment,
        bool IsClosed,
        IReadOnlyList<LineRow> Lines,
        IReadOnlyList<AttachmentRow> Attachments);

    public sealed record LineRow(string Description, long AmountRials, bool HasInvoice, string? Remarks);

    public sealed record AttachmentRow(int Id, string FileName, long FileSizeBytes);
}
