// Employee sign-in: username plus access key, both required and matched to the same record.
// The employee ID embedded in the key only locates the row; the username must match it and the
// full key must verify against the stored hash. Failures are generic (no hint whether the
// username exists) and counted by LoginThrottlingService per client address and per username.
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    ILookupNormalizer lookupNormalizer,
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

    /// <summary>Validates username + access key and creates the employee cookie session.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        ReadSignOutReason();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var accountKey = ILoginThrottlingService.AccountKey("employee", Input.Username);
        if (throttling.IsBlocked(ipAddress, accountKey))
        {
            ModelState.AddModelError(string.Empty, "Too many sign-in attempts. Try again later.");
            return Page();
        }

        var normalizedUsername = lookupNormalizer.NormalizeName(Input.Username.Trim());
        Employee? employee = null;
        if (accessKeyService.TryGetEmployeeId(Input.AccessKey, out var employeeId))
        {
            employee = await dbContext.Employees.SingleOrDefaultAsync(
                item => item.Id == employeeId, HttpContext.RequestAborted);
        }

        // Every branch fails with the same message so neither username nor key existence leaks.
        if (employee is null
            || !string.Equals(employee.NormalizedUsername, normalizedUsername, StringComparison.Ordinal)
            || employee.AccessStatus != EmployeeAccessStatus.Active
            || !accessKeyService.Verify(employee, Input.AccessKey))
        {
            throttling.RecordFailure(ipAddress, accountKey);
            ModelState.AddModelError(string.Empty, "The username or access key is incorrect.");
            return Page();
        }

        throttling.RecordSuccess(ipAddress, accountKey);

        // Claims come from the verified database record, never from editable profile fields.
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
        [Required(ErrorMessage = "Username is required.")]
        [StringLength(100)]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Access key is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Employee access key")]
        public string AccessKey { get; set; } = string.Empty;
    }
}
