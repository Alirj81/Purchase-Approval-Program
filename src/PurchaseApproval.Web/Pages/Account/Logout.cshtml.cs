// Requires a signed-in privileged user before logout is processed.
using Microsoft.AspNetCore.Authorization;
// Provides the Identity sign-in manager used to clear the privileged cookie.
using Microsoft.AspNetCore.Identity;
// Supplies the redirect result returned by the handler.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Models;

namespace PurchaseApproval.Web.Pages.Account;

/// <summary>Ends an administrator or accountant Identity session.</summary>
[Authorize]
public sealed class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Index");
    }
}
