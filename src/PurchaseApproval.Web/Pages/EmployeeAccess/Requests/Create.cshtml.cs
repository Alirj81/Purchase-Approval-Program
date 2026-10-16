using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

/// <summary>
/// Purpose: Receives and validates the basic employee payment-request form.
/// Saving requests is added separately; this page currently checks details only.
/// </summary>
// Security: Select the employee cookie so a staff login alone cannot open this page.
// The cookie validation event also rejects disabled, revoked, or outdated employee sessions.
[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
// Security: Avoid caching responses containing payment details in browsers or shared caches.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(IEmployeeProfileService employeeProfiles) : PageModel
{
    // Security: This is loaded from the database and is never bound from form fields.
    // The employee cannot change the requester identity by editing a browser input.
    public EmployeeProfile? Requester { get; private set; }

    // Security: Bind only editable fields, not the database entity. This prevents submitted
    // values from setting ownership, approval status, or other server-managed properties.
    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Purpose: Shows validation feedback for this response; it never indicates a saved request.
    public bool DetailsChecked { get; private set; }

    /// <summary>Purpose: Displays an empty form with the signed-in employee's current profile.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadRequesterAsync() ? Page() : EndEmployeeSession();
    }

    /// <summary>Purpose: Checks submitted details and displays validation errors.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        // Security: Re-read the database profile on every POST, even if the form has errors.
        // This both preserves the profile display and prevents trusting posted requester details.
        if (!await LoadRequesterAsync())
        {
            return EndEmployeeSession();
        }

        // Technique: Razor Pages applies validation attributes before calling this handler.
        // ModelState collects the results, simplifying the server-side validation check.
        if (!ModelState.IsValid)
        {
            return Page();
        }

        DetailsChecked = true;
        return Page();
    }

    /// <summary>Purpose: Loads profile details using only the authenticated employee's claims.</summary>
    private async Task<bool> LoadRequesterAsync()
    {
        // Security: Use the employee identity specifically; staff and employee IDs can overlap.
        var identity = User.Identities.FirstOrDefault(item => item.IsAuthenticated
            && item.AuthenticationType == EmployeeAuthenticationDefaults.AuthenticationScheme);
        var employeeIdValue = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var credentialVersionValue = identity?.FindFirst(
            EmployeeAuthenticationDefaults.CredentialVersionClaim)?.Value;

        // Technique: TryParse rejects missing or malformed claims without throwing an exception.
        if (!int.TryParse(employeeIdValue, out var employeeId) || employeeId <= 0
            || !int.TryParse(credentialVersionValue, out var credentialVersion) || credentialVersion <= 0)
        {
            return false;
        }

        Requester = await employeeProfiles.GetActiveProfileAsync(
            employeeId, credentialVersion, HttpContext.RequestAborted);
        return Requester is not null;
    }

    /// <summary>Security: Clears an unusable employee session and returns to the access-key form.</summary>
    private static SignOutResult EndEmployeeSession()
        => new(EmployeeAuthenticationDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = "/" });

    /// <summary>Structure: Groups only the fields employees can edit in this form.</summary>
    public sealed class InputModel
    {
        // Purpose: Identifies the recipient of the payment, not the employee requesting it.
        // Technique: Attribute limits match database columns to catch mistakes before saving.
        [Required(ErrorMessage = "Enter the payee's name.")]
        [StringLength(200)]
        [Display(Name = "Payee name")]
        public string PayeeName { get; set; } = string.Empty;

        // Purpose: Records the reason for the payment so a manager can review it later.
        [Required(ErrorMessage = "Enter the reason for this payment.")]
        [StringLength(2000)]
        [Display(Name = "Payment reason")]
        public string Justification { get; set; } = string.Empty;

        // Syntax: string? permits missing details. Text also preserves leading zeroes
        // in account/card numbers that would be lost if stored as a numeric value.
        [StringLength(100)]
        [Display(Name = "Account or card number (optional)")]
        public string? RequestedAccountOrCardNumber { get; set; }
    }
}
