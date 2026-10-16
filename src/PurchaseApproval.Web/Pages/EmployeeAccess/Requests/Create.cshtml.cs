using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Security;

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
public sealed class CreateModel : PageModel
{
    // Security: Bind only editable fields, not the database entity. This prevents submitted
    // values from setting ownership, approval status, or other server-managed properties.
    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Purpose: Shows validation feedback for this response; it never indicates a saved request.
    public bool DetailsChecked { get; private set; }

    /// <summary>Purpose: Displays an empty form when the employee opens the page.</summary>
    public void OnGet()
    {
    }

    /// <summary>Purpose: Checks submitted details and displays validation errors.</summary>
    public IActionResult OnPost()
    {
        // Technique: Razor Pages applies validation attributes before calling this handler.
        // ModelState collects the results, simplifying the server-side validation check.
        if (!ModelState.IsValid)
        {
            return Page();
        }

        DetailsChecked = true;
        return Page();
    }

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
