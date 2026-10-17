using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
// Signs employees into the separate access-key cookie.
using Microsoft.AspNetCore.Authentication;
// Allows anonymous access to the initial key form while protecting redirects.
using Microsoft.AspNetCore.Authorization;
// Provides binding and redirect results for the form handler.
using Microsoft.AspNetCore.Mvc;
// Supplies the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
// Queries the employee record used to verify the submitted key.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages;

/// <summary>Displays the home key form and starts employee sessions after verifying access.</summary>
[AllowAnonymous]
public sealed class IndexModel(
    ApplicationDbContext dbContext,
    IEmployeeAccessKeyService accessKeyService) : PageModel
{
    // BindProperty fills this input model from the submitted form, avoiding manual parsing.
    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Remembers the requested page when authentication redirects an employee here.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    /// <summary>Validates an access key and creates the employee cookie session.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // The ID inside the key only locates a record. The full key must still be verified.
        if (!accessKeyService.TryGetEmployeeId(Input.AccessKey, out var employeeId))
        {
            ModelState.AddModelError(string.Empty, "The access key is incorrect.");
            return Page();
        }

        var employee = await dbContext.Employees.SingleOrDefaultAsync(
            item => item.Id == employeeId);

        if (employee is null
            || employee.AccessStatus != EmployeeAccessStatus.Active
            || !accessKeyService.Verify(employee, Input.AccessKey))
        {
            ModelState.AddModelError(string.Empty, "The access key is incorrect.");
            return Page();
        }

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
        await dbContext.SaveChangesAsync();

        // Only allow local destinations so a supplied return URL cannot redirect to another site.
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/employee");
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Access key is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Employee access key")]
        public string AccessKey { get; set; } = string.Empty;
    }
}
