// Provides the not-found and page results used by tracking.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
// Reads only the request status fields needed for the public tracking page.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
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
                item.TotalAmountRials,
                item.SubmittedAtUtc,
                item.TrackingTokenHash
            })
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        if (request is null
            || !PaymentRequestSubmissionRules.VerifyTrackingToken(token, request.TrackingTokenHash))
            return NotFound();

        RequestStatus = new TrackedRequest(request.ReferenceNumber, request.Status.ToString(),
            request.TotalAmountRials, request.SubmittedAtUtc);
        return Page();
    }

    public sealed record TrackedRequest(string ReferenceNumber, string Status,
        long TotalAmountRials, DateTimeOffset SubmittedAtUtc);
}
