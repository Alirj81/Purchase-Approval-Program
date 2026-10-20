// Explicit confirmation step for restoring a revoked employee. GET shows what will happen;
// POST performs the restoration through EmployeeAccessService and shows the fresh key once.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Employees;

[Authorize(Policy = AppPolicies.CanManageEmployees)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RestoreModel(
    ApplicationDbContext dbContext,
    IEmployeeAccessService employeeAccess,
    UserManager<ApplicationUser> userManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public string FullName { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? IssuedAccessKey { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        return await LoadAsync(id) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(int id, bool confirmed)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (!confirmed)
        {
            ModelState.AddModelError(string.Empty, localizer["Tick the confirmation box to restore access."]);
            return Page();
        }

        var administrator = await userManager.GetUserAsync(User);
        if (administrator is null || !administrator.IsActive)
        {
            return Forbid();
        }

        var result = await employeeAccess.RestoreAccessAsync(
            id,
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

        IssuedAccessKey = result.IssuedAccessKey;
        return Page();
    }

    private async Task<bool> LoadAsync(int id)
    {
        var employee = await dbContext.Employees.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.FullName, item.Username, item.AccessStatus, item.RevokedAtUtc })
            .SingleOrDefaultAsync(HttpContext.RequestAborted);
        if (employee is null)
        {
            return false;
        }

        FullName = employee.FullName;
        Username = employee.Username;
        RevokedAtUtc = employee.RevokedAtUtc;
        return true;
    }
}
