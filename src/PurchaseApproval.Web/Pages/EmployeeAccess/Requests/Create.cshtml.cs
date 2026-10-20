// Employee payment-request form. The page re-checks the employee session on every call, copies
// uploaded files into memory within the configured size, and hands everything to the submission
// service, which validates, enforces the daily quota and saves atomically. Rule messages come
// back as localizable keys and are translated here before display.
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
[EnableRateLimiting("submission-burst")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(
    IEmployeeProfileService employeeProfiles,
    IPaymentRequestSubmissionService submissions,
    IOptions<ApplicationLimitsOptions> limits,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>Profile of the employee making the request, read from the database each time.</summary>
    public EmployeeProfile? Requester { get; private set; }

    public ApplicationLimitsOptions Limits => limits.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Attachments { get; set; } = [];

    [BindProperty]
    public string SubmissionKey { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadRequesterAsync()) return EndEmployeeSession();
        SubmissionKey = PaymentRequestSubmissionRules.CreateSubmissionKey();
        Input.Lines.Add(new LineInput());
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadRequesterAsync()) return EndEmployeeSession();

        if (Input.Lines.Count > PaymentRequestSubmissionRules.MaximumLines)
            ModelState.AddModelError("Input.Lines", localizer["Too many expense rows were submitted."]);
        if (Attachments.Count > Limits.MaximumAttachmentCount)
            ModelState.AddModelError(nameof(Attachments), localizer["Attach no more than {0} files.", Limits.MaximumAttachmentCount]);
        if (!ModelState.IsValid) return Page();

        var uploadedFiles = await RequestFormFiles.ReadAsync(
            Attachments, Limits.MaximumAttachmentBytes, ModelState, localizer, HttpContext.RequestAborted);
        if (!ModelState.IsValid) return Page();

        try
        {
            var result = await submissions.SubmitAsync(
                Requester!,
                Input.Lines.Select(line => new PaymentRequestLineInput(
                    line.Description, line.AmountRials, line.HasInvoice, line.Remarks)).ToList(),
                uploadedFiles,
                Input.PayeeName,
                Input.Justification,
                Input.RequestedAccountOrCardNumber,
                SubmissionKey,
                HttpContext.RequestAborted);

            if (result.TrackingToken is not null)
            {
                TempData["TrackingToken"] = result.TrackingToken;
            }
            if (result.IsDuplicate)
            {
                TempData["StatusMessage"] = "This form was already submitted; no duplicate request was created.";
            }
            return RedirectToPage("Submitted", new { id = result.RequestId });
        }
        catch (ValidationMessagesException exception)
        {
            foreach (var message in exception.Messages)
            {
                ModelState.AddModelError(string.Empty, localizer[message.Key, message.Args]);
            }

            return Page();
        }
    }

    private async Task<bool> LoadRequesterAsync()
    {
        var identity = User.Identities.FirstOrDefault(item => item.IsAuthenticated
            && item.AuthenticationType == EmployeeAuthenticationDefaults.AuthenticationScheme);
        var employeeIdValue = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var credentialVersionValue = identity?.FindFirst(
            EmployeeAuthenticationDefaults.CredentialVersionClaim)?.Value;

        if (!int.TryParse(employeeIdValue, out var employeeId) || employeeId <= 0
            || !int.TryParse(credentialVersionValue, out var credentialVersion) || credentialVersion <= 0)
            return false;

        Requester = await employeeProfiles.GetActiveProfileAsync(
            employeeId, credentialVersion, HttpContext.RequestAborted);
        return Requester is not null;
    }

    private static SignOutResult EndEmployeeSession()
        => new(EmployeeAuthenticationDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = "/" });

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter the payee's name.")]
        [StringLength(200)]
        [Display(Name = "Payee name")]
        public string PayeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the reason for this payment.")]
        [StringLength(2000)]
        [Display(Name = "Payment reason")]
        public string Justification { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Account or card number (optional)")]
        public string? RequestedAccountOrCardNumber { get; set; }

        [MinLength(1, ErrorMessage = "Add at least one expense row.")]
        public List<LineInput> Lines { get; set; } = [];
    }

    public sealed class LineInput
    {
        [Required(ErrorMessage = "Enter an expense description.")]
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Range(1, PaymentRequestSubmissionRules.MaximumTotalRials,
            ErrorMessage = "Enter a positive whole-rial amount.")]
        public long AmountRials { get; set; }

        public bool HasInvoice { get; set; }

        [StringLength(1000)]
        public string? Remarks { get; set; }
    }
}
