using System.ComponentModel.DataAnnotations;
// Allows the login page to be opened before authentication.
using Microsoft.AspNetCore.Authorization;
// Validates passwords and applies Identity lockout rules.
using Microsoft.AspNetCore.Identity;
// Supplies form binding and redirect results.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Models;

namespace PurchaseApproval.Web.Pages.Account;

/// <summary>Authenticates administrators and accountants through ASP.NET Core Identity.</summary>
[AllowAnonymous]
public sealed class LoginModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    // The input model groups editable login fields without binding an ApplicationUser entity.
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    /// <summary>Validates credentials, lockout state, and the application's active flag.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByNameAsync(Input.Username.Trim());
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "The username or password is incorrect.");
            return Page();
        }

        // Identity checks the password hash and counts failed attempts for account lockout.
        // Reusing Identity avoids implementing password comparison and failure counters ourselves.
        var checkResult = await signInManager.CheckPasswordSignInAsync(
            user,
            Input.Password,
            lockoutOnFailure: true);

        if (!checkResult.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                checkResult.IsLockedOut
                    ? "The account is temporarily locked."
                    : "The username or password is incorrect.");
            return Page();
        }

        user.LastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
        await signInManager.SignInAsync(user, Input.RememberMe);

        // Restrict redirects to this application, even if the return URL came from a query string.
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
