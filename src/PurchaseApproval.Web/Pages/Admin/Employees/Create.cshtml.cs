// Creates an employee profile and issues its first one-time access key through
// EmployeeAccessService, which also writes the audit entry.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Employees;

[Authorize(Policy = AppPolicies.CanManageEmployees)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(
    IEmployeeAccessService employeeAccess,
    UserManager<ApplicationUser> userManager,
    IOptions<ApplicationLimitsOptions> limits,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? IssuedAccessKey { get; private set; }

    public bool BranchRequired => limits.Value.RequireEmployeeBranch;

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

        var result = await employeeAccess.CreateAsync(
            Input.Username,
            new EmployeeProfileInput(
                Input.FullName, Input.PhoneNumber, Input.Department, Input.Branch, Input.JobTitle, Input.AdminNotes),
            new AuditActor(administrator.Id, administrator.FullName, AppRoles.Administrator),
            HttpContext.RequestAborted);

        if (result.Status != EmployeeAccessOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    result.Status == EmployeeAccessOperation.UsernameTaken ? "Input.Username" : string.Empty,
                    localizer[error.Key, error.Args]);
            }

            return Page();
        }

        IssuedAccessKey = result.IssuedAccessKey;
        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Full name is required."), StringLength(200)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Username is required."), StringLength(100, MinimumLength = 3)]
        [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "Use letters, numbers, periods, hyphens, or underscores only.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [StringLength(50)]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [StringLength(100)]
        [Display(Name = "Department")]
        public string? Department { get; set; }

        [StringLength(100)]
        [Display(Name = "Branch")]
        public string? Branch { get; set; }

        [StringLength(100)]
        [Display(Name = "Job title")]
        public string? JobTitle { get; set; }

        [StringLength(1000)]
        [Display(Name = "Administrator notes (private)")]
        public string? AdminNotes { get; set; }
    }
}
