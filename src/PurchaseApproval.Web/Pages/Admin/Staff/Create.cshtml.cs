// Creates one administrator or accountant account with a temporary password shown once.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Staff;

[Authorize(Policy = AppPolicies.CanManageEmployees)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(
    IStaffAccountService staffAccounts,
    UserManager<ApplicationUser> userManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? TemporaryPassword { get; private set; }

    public DateTimeOffset? TemporaryPasswordExpiresAtUtc { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var administrator = await userManager.GetUserAsync(User);
        if (administrator is null || !administrator.IsActive)
        {
            return Forbid();
        }

        var result = await staffAccounts.CreateAsync(
            new StaffAccountInput(Input.Username, Input.FullName, Input.Role, Input.Department, Input.Branch, Input.JobTitle),
            new AuditActor(administrator.Id, administrator.FullName, AppRoles.Administrator),
            HttpContext.RequestAborted);

        if (result.Status != StaffAccountOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, localizer[error.Key, error.Args]);
            }

            return Page();
        }

        TemporaryPassword = result.TemporaryPassword;
        TemporaryPasswordExpiresAtUtc = result.ExpiresAtUtc;
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Username is required."), StringLength(100, MinimumLength = 3)]
        [RegularExpression("^[A-Za-z0-9._@-]+$", ErrorMessage = "Use letters, numbers, periods, hyphens, underscores or @ only.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full name is required."), StringLength(200)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose Administrator or Accountant.")]
        [Display(Name = "Role")]
        public string Role { get; set; } = AppRoles.Accountant;

        [StringLength(100)]
        [Display(Name = "Department")]
        public string? Department { get; set; }

        [StringLength(100)]
        [Display(Name = "Branch")]
        public string? Branch { get; set; }

        [StringLength(100)]
        [Display(Name = "Job title")]
        public string? JobTitle { get; set; }
    }
}
