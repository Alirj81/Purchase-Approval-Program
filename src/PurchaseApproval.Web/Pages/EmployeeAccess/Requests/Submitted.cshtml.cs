using System.Security.Claims;
// Requires the employee session before showing the receipt.
using Microsoft.AspNetCore.Authorization;
// Supplies the page result and route helpers used by the receipt page.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
// Loads the submitted request without tracking it for changes.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubmittedModel(ApplicationDbContext dbContext) : PageModel
{
    public RequestReceipt? Receipt { get; private set; }
    public string? TrackingUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Forbid();

        Receipt = await dbContext.PaymentRequests.AsNoTracking()
            .Where(item => item.Id == id && item.EmployeeRequesterId == employeeId)
            .Select(item => new RequestReceipt(
                item.ReferenceNumber, item.TotalAmountRials, item.Status, item.SubmittedAtUtc))
            .SingleOrDefaultAsync(HttpContext.RequestAborted);
        if (Receipt is null) return NotFound();

        // The token lives only in TempData for one redirect; it is never stored in the database.
        if (TempData["TrackingToken"] is string token)
        {
            TrackingUrl = Url.PageLink("/Track", values: new
            {
                reference = Receipt.ReferenceNumber,
                token
            });
        }

        return Page();
    }

    private bool TryGetEmployeeId(out int employeeId)
    {
        var identity = User.Identities.FirstOrDefault(item => item.IsAuthenticated
            && item.AuthenticationType == EmployeeAuthenticationDefaults.AuthenticationScheme);
        return int.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out employeeId)
            && employeeId > 0;
    }

    public sealed record RequestReceipt(
        string ReferenceNumber,
        long TotalAmountRials,
        PaymentRequestStatus Status,
        DateTimeOffset SubmittedAtUtc);
}
