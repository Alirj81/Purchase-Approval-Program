// Edits an employee's structured profile. The username is deliberately read-only: it is the
// sign-in identifier and part of every request snapshot. Saved changes are audited; requests
// already submitted keep their original snapshot.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Employees;

[Authorize(Policy = AppPolicies.CanManageEmployees)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EditModel(
    ApplicationDbContext dbContext,
    IEmployeeAccessService employeeAccess,
    UserManager<ApplicationUser> userManager,
    IOptions<ApplicationLimitsOptions> limits,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Username { get; private set; } = string.Empty;

    public EmployeeAccessStatus AccessStatus { get; private set; }

    public bool BranchRequired => limits.Value.RequireEmployeeBranch;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var employee = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, HttpContext.RequestAborted);
        if (employee is null)
        {
            return NotFound();
        }

        Username = employee.Username;
        AccessStatus = employee.AccessStatus;
        Input = new InputModel
        {
            FullName = employee.FullName,
            PhoneNumber = employee.PhoneNumber,
            Department = employee.Department,
            Branch = employee.Branch,
            JobTitle = employee.JobTitle,
            AdminNotes = employee.AdminNotes
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var employee = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, HttpContext.RequestAborted);
        if (employee is null)
        {
            return NotFound();
        }

        Username = employee.Username;
        AccessStatus = employee.AccessStatus;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var administrator = await userManager.GetUserAsync(User);
        if (administrator is null || !administrator.IsActive)
        {
            return Forbid();
        }

        var result = await employeeAccess.UpdateProfileAsync(
            id,
            new EmployeeProfileInput(
                Input.FullName, Input.PhoneNumber, Input.Department, Input.Branch, Input.JobTitle, Input.AdminNotes),
            new AuditActor(administrator.Id, administrator.FullName, AppRoles.Administrator),
            HttpContext.RequestAborted);

        if (result.Status != EmployeeAccessOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, localizer[error.Key, error.Args]);
            }

            return Page();
        }

        TempData["StatusMessage"] = "The employee profile was updated.";
        return RedirectToPage("Index");
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Full name is required."), StringLength(200)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

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
