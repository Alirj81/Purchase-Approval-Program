// Signs out the employee-specific cookie scheme.
using Microsoft.AspNetCore.Authentication;
// Ensures only an authenticated employee can reach this handler.
using Microsoft.AspNetCore.Authorization;
// Supplies the redirect result.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.EmployeeAccess;

/// <summary>Ends an employee access-key cookie session.</summary>
[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(EmployeeAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }
}
