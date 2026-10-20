// Private bearer-token status page. The token proves nothing about who is asking, so the page
// shows only the reference, the decision status and the minimal payment/closure labels. No
// amounts, payee, rows, attachments, explanations or names ever appear here.
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TrackModel(ApplicationDbContext dbContext) : PageModel
{
    public TrackedRequest? RequestStatus { get; private set; }

    public async Task<IActionResult> OnGetAsync(string reference, string token)
    {
        var request = await dbContext.PaymentRequests.AsNoTracking()
            .Where(item => item.ReferenceNumber == reference)
            .Select(item => new
            {
                item.ReferenceNumber,
                item.Status,
                item.TrackingTokenHash,
                Transfer = item.TransferOutcome == null ? null : (TransferStatus?)item.TransferOutcome.Status,
                IsClosed = item.TransferOutcome != null && item.TransferOutcome.IsClosed
            })
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        // Unknown reference and wrong token produce the same 404 so nothing can be enumerated.
        if (request is null
            || !PaymentRequestSubmissionRules.VerifyTrackingToken(token, request.TrackingTokenHash))
            return NotFound();

        RequestStatus = new TrackedRequest(
            request.ReferenceNumber,
            request.Status,
            request.Status == PaymentRequestStatus.Approved
                ? request.Transfer ?? TransferStatus.AwaitingTransfer
                : null,
            request.IsClosed);
        return Page();
    }

    public sealed record TrackedRequest(
        string ReferenceNumber,
        PaymentRequestStatus Status,
        TransferStatus? Payment,
        bool IsClosed);
}
