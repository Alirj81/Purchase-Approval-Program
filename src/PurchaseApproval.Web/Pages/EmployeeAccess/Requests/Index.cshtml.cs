using System.Security.Claims;
// Requires the employee cookie before request history is shown.
using Microsoft.AspNetCore.Authorization;
// Supplies the Forbid result and page response.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
// Projects only the employee's own request history.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(ApplicationDbContext dbContext) : PageModel
{
    public IReadOnlyList<RequestRow> Requests { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var identity = User.Identities.FirstOrDefault(item => item.IsAuthenticated
            && item.AuthenticationType == EmployeeAuthenticationDefaults.AuthenticationScheme);
        if (!int.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var employeeId)
            || employeeId <= 0)
            return Forbid();

        Requests = await dbContext.PaymentRequests.AsNoTracking()
            .Where(item => item.EmployeeRequesterId == employeeId)
            .OrderByDescending(item => item.SubmittedAtUtc)
            .Select(item => new RequestRow(item.Id, item.ReferenceNumber, item.PayeeName,
                item.TotalAmountRials, item.Status.ToString(), item.SubmittedAtUtc))
            .Take(100)
            .ToListAsync(HttpContext.RequestAborted);
        return Page();
    }

    public sealed record RequestRow(int Id, string ReferenceNumber, string PayeeName,
        long TotalAmountRials, string Status, DateTimeOffset SubmittedAtUtc);
}
