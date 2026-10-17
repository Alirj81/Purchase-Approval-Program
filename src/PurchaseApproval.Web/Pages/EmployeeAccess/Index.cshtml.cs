using System.Security.Claims;
// Restricts the dashboard to the employee cookie scheme.
using Microsoft.AspNetCore.Authorization;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.EmployeeAccess;

/// <summary>Landing page proving that an employee cookie is authenticated.</summary>
[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
public sealed class IndexModel : PageModel
{
    public string FullName { get; private set; } = string.Empty;

    public void OnGet()
    {
        FullName = User.FindFirstValue(ClaimTypes.GivenName) ?? User.Identity?.Name ?? string.Empty;
    }
}
