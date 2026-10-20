// Staff sign-in through ASP.NET Core Identity, with IP/account throttling in front of the
// password check and the temporary-credential rule behind it: an expired temporary password
// fails like any wrong password, and an unexpired one leads straight to the change-password page.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Account;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoginModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILoginThrottlingService throttling,
    TimeProvider timeProvider) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var accountKey = ILoginThrottlingService.AccountKey("staff", Input.Username);
        if (throttling.IsBlocked(ipAddress, accountKey))
        {
            ModelState.AddModelError(string.Empty, "Too many sign-in attempts. Try again later.");
            return Page();
        }

        var user = await userManager.FindByNameAsync(Input.Username.Trim());
        if (user is null || !user.IsActive)
        {
            throttling.RecordFailure(ipAddress, accountKey);
            ModelState.AddModelError(string.Empty, "The username or password is incorrect.");
            return Page();
        }

        // Identity checks the password hash and counts failed attempts for account lockout.
        var checkResult = await signInManager.CheckPasswordSignInAsync(
            user,
            Input.Password,
            lockoutOnFailure: true);

        var now = timeProvider.GetUtcNow();
        var temporaryExpired = user.MustChangePassword
            && user.TemporaryCredentialExpiresAtUtc is { } expiry && expiry < now;
        if (!checkResult.Succeeded || temporaryExpired)
        {
            throttling.RecordFailure(ipAddress, accountKey);
            ModelState.AddModelError(
                string.Empty,
                checkResult.IsLockedOut
                    ? "The account is temporarily locked."
                    : "The username or password is incorrect.");
            return Page();
        }

        throttling.RecordSuccess(ipAddress, accountKey);
        user.LastAuthenticatedAtUtc = now;
        await userManager.UpdateAsync(user);
        await signInManager.SignInAsync(user, Input.RememberMe && !user.MustChangePassword);

        if (user.MustChangePassword)
        {
            return RedirectToPage("/Account/ChangePassword", new { required = true });
        }

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
