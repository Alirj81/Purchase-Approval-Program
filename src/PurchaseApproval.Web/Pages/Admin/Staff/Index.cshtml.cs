// Administrator-only list of staff accounts with deactivate/reactivate, role change and
// password-reset actions. All rules (last active administrator, self-actions, session
// invalidation, auditing) live in StaffAccountService; this page only maps results to messages
// and shows a temporary password exactly once in the response that created it.
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
public sealed class IndexModel(
    IStaffAccountService staffAccounts,
    UserManager<ApplicationUser> userManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public IReadOnlyList<StaffAccountRow> Accounts { get; private set; } = [];

    public int CurrentUserId { get; private set; }

    public string? TemporaryPassword { get; private set; }

    public string? TemporaryPasswordFor { get; private set; }

    public DateTimeOffset? TemporaryPasswordExpiresAtUtc { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return Forbid();
        }

        return Page();
    }

    public Task<IActionResult> OnPostDeactivateAsync(int id)
        => RunAsync(actor => staffAccounts.DeactivateAsync(id, actor, HttpContext.RequestAborted), "The account was deactivated.");

    public Task<IActionResult> OnPostReactivateAsync(int id)
        => RunAsync(actor => staffAccounts.ReactivateAsync(id, actor, HttpContext.RequestAborted), "The account was reactivated.");

    public Task<IActionResult> OnPostChangeRoleAsync(int id, string role)
        => RunAsync(actor => staffAccounts.ChangeRoleAsync(id, role ?? string.Empty, actor, HttpContext.RequestAborted), "The role was changed. The account holder must sign in again.");

    public async Task<IActionResult> OnPostResetPasswordAsync(int id)
    {
        var actor = await ResolveActorAsync();
        if (actor is null)
        {
            return Forbid();
        }

        var result = await staffAccounts.InitiatePasswordResetAsync(id, actor, HttpContext.RequestAborted);
        if (result.Status == StaffAccountOperation.NotFound)
        {
            return NotFound();
        }

        if (result.Status != StaffAccountOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, localizer[error.Key, error.Args]);
            }
        }
        else
        {
            TemporaryPassword = result.TemporaryPassword;
            TemporaryPasswordExpiresAtUtc = result.ExpiresAtUtc;
            TemporaryPasswordFor = (await staffAccounts.GetAsync(id, HttpContext.RequestAborted))?.Username;
        }

        await LoadAsync();
        return Page();
    }

    private async Task<IActionResult> RunAsync(
        Func<AuditActor, Task<StaffOperationResult>> operation,
        string successMessage)
    {
        var actor = await ResolveActorAsync();
        if (actor is null)
        {
            return Forbid();
        }

        var result = await operation(actor);
        if (result.Status == StaffAccountOperation.NotFound)
        {
            return NotFound();
        }

        if (result.Status != StaffAccountOperation.Success)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, localizer[error.Key, error.Args]);
            }

            await LoadAsync();
            return Page();
        }

        TempData["StatusMessage"] = successMessage;
        return RedirectToPage();
    }

    private async Task<AuditActor?> ResolveActorAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        CurrentUserId = user.Id;
        return new AuditActor(user.Id, user.FullName, AppRoles.Administrator);
    }

    private async Task<bool> LoadAsync()
    {
        if (CurrentUserId == 0 && await ResolveActorAsync() is null)
        {
            return false;
        }

        Accounts = await staffAccounts.ListAsync(HttpContext.RequestAborted);
        return true;
    }
}
