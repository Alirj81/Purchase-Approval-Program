// This file lets an authenticated administrator submit a payment request using their own profile.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
// Identity retrieves the trusted administrator record instead of accepting requester fields from the browser.
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Requests;

/// <summary>Creates administrator-owned payment requests that are stored separately from employee requests.</summary>
[Authorize(Policy = AppPolicies.CanDecideRequests)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(
    UserManager<ApplicationUser> userManager,
    IPaymentRequestSubmissionService submissions) : PageModel
{
    /// <summary>Trusted profile shown as read-only requester information.</summary>
    public ApplicationUserProfile? Requester { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Attachments { get; set; } = [];

    /// <summary>Loads the signed-in administrator and starts the form with one expense row.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadRequesterAsync())
        {
            return Forbid();
        }

        Input.Lines.Add(new LineInput());
        return Page();
    }

    /// <summary>Validates files and saves a request that belongs to the signed-in administrator.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadRequesterAsync())
        {
            return Forbid();
        }

        if (Input.Lines.Count > PaymentRequestSubmissionRules.MaximumLines)
        {
            ModelState.AddModelError("Input.Lines", "Too many expense rows were submitted.");
        }

        if (Attachments.Count > PaymentRequestSubmissionRules.MaximumAttachments)
        {
            ModelState.AddModelError(nameof(Attachments), "Attach no more than five files.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

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
            uploadedFiles.Add(new PaymentRequestAttachmentInput(
                file.FileName, file.ContentType, stream.ToArray()));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

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
                HttpContext.RequestAborted);

            TempData["StatusMessage"] = "Your payment request was submitted for review.";
            return RedirectToPage("Details", new { id = result.RequestId });
        }
        catch (PaymentRequestValidationException exception)
        {
            foreach (var error in exception.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }
    }

    /// <summary>Builds the requester snapshot from the active Identity account, never posted form values.</summary>
    private async Task<bool> LoadRequesterAsync()
    {
        var administrator = await userManager.GetUserAsync(User);
        if (administrator is null || !administrator.IsActive || string.IsNullOrWhiteSpace(administrator.UserName))
        {
            return false;
        }

        Requester = new ApplicationUserProfile(
            administrator.Id,
            administrator.FullName,
            administrator.UserName,
            administrator.PhoneNumber,
            administrator.Department,
            administrator.Branch,
            administrator.JobTitle);
        return true;
    }

    /// <summary>Contains only fields the administrator may enter on the request form.</summary>
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

    /// <summary>Represents one expense row posted with the request.</summary>
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
