// Employee sign-in: the access key alone. The key has the form pak_<employeeId>_<256-bit secret>;
// the ID part only locates the row, the secret must verify against the stored salted hash, and
// the account must be Active. Employees never type a username (it stays an internal identifier).
// Failures are generic and counted by LoginThrottlingService per client address and, when the
// key names an account, per that account ID, never per key value.
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(
    ApplicationDbContext dbContext,
    IEmployeeAccessKeyService accessKeyService,
    ILoginThrottlingService throttling) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Remembers the requested page when authentication redirects an employee here.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>"access-changed" when the previous session was ended by an administrator action.</summary>
    public string? SignOutReason { get; private set; }

    public IActionResult OnGet()
    {
        // Signed-in employees go straight to their dashboard; staff are redirected to the
        // review or execution area so the key form is only shown to visitors.
        if (User.HasClaim(EmployeeAuthenticationDefaults.AuthenticationKindClaim,
                EmployeeAuthenticationDefaults.AuthenticationKind))
        {
            return RedirectToPage("/EmployeeAccess/Index");
        }

        if (User.IsInRole(AppRoles.Administrator))
        {
            return RedirectToPage("/Admin/Requests/Index");
        }

        if (User.IsInRole(AppRoles.Accountant))
        {
            return RedirectToPage("/Execution/Index");
        }

        ReadSignOutReason();
        return Page();
    }

    /// <summary>Validates the access key and creates the employee cookie session.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        ReadSignOutReason();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        // The account counter is keyed by the ID the key claims to belong to (a non-secret
        // number), so guessing against one employee is slowed down even from many addresses.
        var hasEmployeeId = accessKeyService.TryGetEmployeeId(Input.AccessKey, out var employeeId);
        var accountKey = hasEmployeeId ? ILoginThrottlingService.AccountKey("employee", $"#{employeeId}") : null;
        if (throttling.IsBlocked(ipAddress, accountKey))
        {
            ModelState.AddModelError(string.Empty, "Too many sign-in attempts. Try again later.");
            return Page();
        }

        Employee? employee = null;
        if (hasEmployeeId)
        {
            employee = await dbContext.Employees.SingleOrDefaultAsync(
                item => item.Id == employeeId, HttpContext.RequestAborted);
        }

        // Every branch fails with the same message so neither the account's existence nor its
        // state (disabled, revoked) leaks to someone holding a wrong or old key.
        if (employee is null
            || employee.AccessStatus != EmployeeAccessStatus.Active
            || !accessKeyService.Verify(employee, Input.AccessKey))
        {
            throttling.RecordFailure(ipAddress, accountKey);
            ModelState.AddModelError(string.Empty, "The access key is incorrect.");
            return Page();
        }

        throttling.RecordSuccess(ipAddress, accountKey);

        // Claims come from the verified database record, never from the submitted form.
        // CredentialVersion lets EmployeeCookieEvents reject an old session after access changes.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
            new(ClaimTypes.Name, employee.Username),
            new(ClaimTypes.GivenName, employee.FullName),
            new(EmployeeAuthenticationDefaults.AuthenticationKindClaim,
                EmployeeAuthenticationDefaults.AuthenticationKind),
            new(EmployeeAuthenticationDefaults.CredentialVersionClaim,
                employee.CredentialVersion.ToString())
        };

        var identity = new ClaimsIdentity(
            claims,
            EmployeeAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            EmployeeAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        employee.LastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

        // Only allow local destinations so a supplied return URL cannot redirect to another site.
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/employee");
    }

    private void ReadSignOutReason()
    {
        if (Request.Cookies.TryGetValue(EmployeeCookieEvents.SignOutReasonCookie, out var reason))
        {
            SignOutReason = reason;
            Response.Cookies.Delete(EmployeeCookieEvents.SignOutReasonCookie);
        }
        else if (!string.IsNullOrEmpty(ReturnUrl))
        {
            SignOutReason = "expired";
        }
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Access key is required.")]
        [StringLength(200)]
        [DataType(DataType.Password)]
        [Display(Name = "Employee access key")]
        public string AccessKey { get; set; } = string.Empty;
    }
}
