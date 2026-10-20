// Lets a signed-in administrator or accountant replace their password. This is also the only
// page a temporary-password holder may use (see ForcedPasswordChangeFilter). After a successful
// change the security stamp rotates, which ends every other session; RefreshSignInAsync keeps
// the current browser signed in.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Account;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IStaffAccountService staffAccounts,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public bool Required { get; set; }

    public DateTimeOffset? TemporaryCredentialExpiresAtUtc { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        Required = Required || user.MustChangePassword;
        TemporaryCredentialExpiresAtUtc = user.TemporaryCredentialExpiresAtUtc;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid();
        }

        Required = Required || user.MustChangePassword;
        TemporaryCredentialExpiresAtUtc = user.TemporaryCredentialExpiresAtUtc;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await staffAccounts.ChangeOwnPasswordAsync(
            user.Id, Input.CurrentPassword, Input.NewPassword, HttpContext.RequestAborted);
        if (result.Status != StaffAccountOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, localizer[error.Key, error.Args]);
            }

            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        TempData["StatusMessage"] = "Your password was changed.";
        return RedirectToPage("/Index");
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current or temporary password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        [DataType(DataType.Password)]
        [StringLength(128, MinimumLength = 12, ErrorMessage = "The password must be at least 12 characters.")]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm the new password.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The new password and its confirmation do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
