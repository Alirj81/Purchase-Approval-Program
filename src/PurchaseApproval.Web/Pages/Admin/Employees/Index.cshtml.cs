using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Employees;

/// <summary>Lists employees and performs credential lifecycle operations.</summary>
[Authorize(Policy = AppPolicies.CanManageEmployees)]
public sealed class IndexModel(
    ApplicationDbContext dbContext,
    IEmployeeAccessKeyService accessKeyService) : PageModel
{
    public IReadOnlyList<EmployeeRow> Employees { get; private set; } = [];

    public string? IssuedAccessKey { get; private set; }

    public string? IssuedForUsername { get; private set; }

    public async Task OnGetAsync() => await LoadEmployeesAsync();

    /// <summary>
    /// Generates a replacement credential. The clear-text key is returned once;
    /// only its password hash is persisted.
    /// </summary>
    public async Task<IActionResult> OnPostRegenerateAsync(int id)
    {
        var employee = await dbContext.Employees.FindAsync(id);
        if (employee is null)
        {
            return NotFound();
        }

        IssuedAccessKey = accessKeyService.RotateCredential(employee);
        IssuedForUsername = employee.Username;
        employee.AccessStatus = EmployeeAccessStatus.Active;
        employee.DisabledAtUtc = null;
        employee.RevokedAtUtc = null;

        await dbContext.SaveChangesAsync();
        await LoadEmployeesAsync();
        return Page();
    }

    /// <summary>Changes access state and invalidates cookies when access is removed.</summary>
    public async Task<IActionResult> OnPostChangeStatusAsync(
        int id,
        EmployeeAccessStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            return BadRequest();
        }

        var employee = await dbContext.Employees.FindAsync(id);
        if (employee is null)
        {
            return NotFound();
        }

        if (employee.AccessStatus == EmployeeAccessStatus.Revoked
            && status != EmployeeAccessStatus.Revoked)
        {
            ModelState.AddModelError(
                string.Empty,
                "Generate a new access key to reactivate a revoked employee.");
            await LoadEmployeesAsync();
            return Page();
        }

        if (employee.AccessStatus != status
            && status is EmployeeAccessStatus.Disabled or EmployeeAccessStatus.Revoked)
        {
            employee.CredentialVersion = checked(employee.CredentialVersion + 1);
        }

        employee.AccessStatus = status;
        employee.DisabledAtUtc = status == EmployeeAccessStatus.Disabled
            ? DateTimeOffset.UtcNow
            : null;
        employee.RevokedAtUtc = status == EmployeeAccessStatus.Revoked
            ? DateTimeOffset.UtcNow
            : null;

        await dbContext.SaveChangesAsync();
        TempData["StatusMessage"] = "The access status was updated.";
        return RedirectToPage();
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
                item.AccessStatus,
                item.CredentialVersion))
            .ToListAsync();
    }

    /// <summary>Read-only table projection that avoids loading credential hashes.</summary>
    public sealed record EmployeeRow(
        int Id,
        string FullName,
        string Username,
        string? Department,
        EmployeeAccessStatus AccessStatus,
        int CredentialVersion);
}
