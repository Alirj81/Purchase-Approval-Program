using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
