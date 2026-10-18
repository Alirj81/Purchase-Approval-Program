using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
// Selects the employee cookie and checks the current session before the form runs.
using Microsoft.AspNetCore.Authentication;
// Restricts this page to authenticated employees.
using Microsoft.AspNetCore.Authorization;
// Supplies page results, binding, and form response types.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class used by this request form.
using Microsoft.AspNetCore.Mvc.RazorPages;
// Limits repeated submissions from the same employee.
using Microsoft.AspNetCore.RateLimiting;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
[EnableRateLimiting("employee-submission")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(
    IEmployeeProfileService employeeProfiles,
    IPaymentRequestSubmissionService submissions) : PageModel
{
    public EmployeeProfile? Requester { get; private set; } /// <summary>
    /// Gets the profile of the employee making the request.
    /// </summary>

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
            ModelState.AddModelError("Input.Lines", "Too many expense rows were submitted.");
        if (Attachments.Count > PaymentRequestSubmissionRules.MaximumAttachments)
            ModelState.AddModelError(nameof(Attachments), "Attach no more than five files.");
        if (!ModelState.IsValid) return Page();

        var uploadedFiles = new List<PaymentRequestAttachmentInput>(Attachments.Count);
        foreach (var file in Attachments)
        {
            if (file.Length is < 1 or > PaymentRequestSubmissionRules.MaximumAttachmentBytes)
            {
                ModelState.AddModelError(nameof(Attachments),
                    $"{file.FileName}: each file must be between 1 byte and 5 MiB.");
                continue;
            }

            await using var stream = new MemoryStream((int)file.Length);
            await file.CopyToAsync(stream, HttpContext.RequestAborted);
            if (stream.Length > PaymentRequestSubmissionRules.MaximumAttachmentBytes)
            {
                ModelState.AddModelError(nameof(Attachments), $"{file.FileName}: file is too large.");
                continue;
            }

            uploadedFiles.Add(new PaymentRequestAttachmentInput(
                file.FileName, file.ContentType, stream.ToArray()));
        }

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
        catch (PaymentRequestValidationException exception)
        {
            foreach (var error in exception.Errors) ModelState.AddModelError(string.Empty, error);
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
