// Employee list with the credential and account-state actions, all delegated to
// EmployeeAccessService so the rules (no silent re-enabling, audited changes) live in one place.
// A replacement key is shown exactly once in the response that generated it.
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

/// <summary>Lists employees and performs credential lifecycle operations.</summary>
[Authorize(Policy = AppPolicies.CanManageEmployees)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(
    ApplicationDbContext dbContext,
    IEmployeeAccessService employeeAccess,
    UserManager<ApplicationUser> userManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public IReadOnlyList<EmployeeRow> Employees { get; private set; } = [];

    public string? IssuedAccessKey { get; private set; }

    public string? IssuedForUsername { get; private set; }

    public async Task OnGetAsync() => await LoadEmployeesAsync();

    /// <summary>Issues a replacement key without changing the account state.</summary>
    public async Task<IActionResult> OnPostReplaceKeyAsync(int id)
    {
        var actor = await ResolveActorAsync();
        if (actor is null)
        {
            return Forbid();
        }

        var result = await employeeAccess.ReplaceKeyAsync(id, actor, HttpContext.RequestAborted);
        return await FinishWithKeyAsync(id, result);
    }

    public Task<IActionResult> OnPostDisableAsync(int id) => ChangeStateAsync(id, employeeAccess.DisableAsync, "The employee was disabled.");

    public Task<IActionResult> OnPostEnableAsync(int id) => ChangeStateAsync(id, employeeAccess.EnableAsync, "The employee was enabled.");

    public Task<IActionResult> OnPostRevokeAsync(int id) => ChangeStateAsync(id, employeeAccess.RevokeAsync, "The employee's access was revoked.");

    public Task<IActionResult> OnPostDeleteAsync(int id) => ChangeStateAsync(
        id, employeeAccess.DeleteAsync, "The employee was deleted. Historical requests and audit records were retained.");

    private async Task<IActionResult> ChangeStateAsync(
        int id,
        Func<int, AuditActor, CancellationToken, Task<EmployeeAccessResult>> operation,
        string successMessage)
    {
        var actor = await ResolveActorAsync();
        if (actor is null)
        {
            return Forbid();
        }

        var result = await operation(id, actor, HttpContext.RequestAborted);
        if (result.Status == EmployeeAccessOperation.NotFound)
        {
            return NotFound();
        }

        if (result.Status != EmployeeAccessOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, localizer[error.Key, error.Args]);
            }

            await LoadEmployeesAsync();
            return Page();
        }

        TempData["StatusMessage"] = successMessage;
        return RedirectToPage();
    }

    private async Task<IActionResult> FinishWithKeyAsync(int id, EmployeeAccessResult result)
    {
        if (result.Status == EmployeeAccessOperation.NotFound)
        {
            return NotFound();
        }

        if (result.Status != EmployeeAccessOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, localizer[error.Key, error.Args]);
            }
        }
        else
        {
            IssuedAccessKey = result.IssuedAccessKey;
            IssuedForUsername = await dbContext.Employees.AsNoTracking()
                .Where(item => item.Id == id)
                .Select(item => item.Username)
                .SingleOrDefaultAsync(HttpContext.RequestAborted);
        }

        await LoadEmployeesAsync();
        return Page();
    }

    private async Task<AuditActor?> ResolveActorAsync()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null || !user.IsActive ? null : new AuditActor(user.Id, user.FullName, AppRoles.Administrator);
    }

    private async Task LoadEmployeesAsync()
    {
        Employees = await dbContext.Employees
            .AsNoTracking()
            .OrderBy(item => item.FullName)
            .Select(item => new EmployeeRow(
                item.Id,
                item.FullName,
                item.Username,
                item.Department,
                item.Branch,
                item.AccessStatus,
                item.CredentialVersion,
                item.LastAuthenticatedAtUtc))
            .ToListAsync();
    }

    /// <summary>Read-only table projection that avoids loading credential hashes or notes.</summary>
    public sealed record EmployeeRow(
        int Id,
        string FullName,
        string Username,
        string? Department,
        string? Branch,
        EmployeeAccessStatus AccessStatus,
        int CredentialVersion,
        DateTimeOffset? LastAuthenticatedAtUtc);
}
