// This file lets an authenticated administrator submit a payment request using their own profile.
// Identity retrieves the trusted administrator record instead of accepting requester fields from
// the browser. Validation, the daily quota and the burst guard apply exactly as for employees.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Requests;

/// <summary>Creates administrator-owned payment requests that are stored separately from employee requests.</summary>
[Authorize(Policy = AppPolicies.CanDecideRequests)]
[EnableRateLimiting("submission-burst")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CreateModel(
    UserManager<ApplicationUser> userManager,
    IPaymentRequestSubmissionService submissions,
    IOptions<ApplicationLimitsOptions> limits,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>Trusted profile shown as read-only requester information.</summary>
    public ApplicationUserProfile? Requester { get; private set; }

    public ApplicationLimitsOptions Limits => limits.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Attachments { get; set; } = [];

    [BindProperty]
    public string SubmissionKey { get; set; } = string.Empty;

    /// <summary>Loads the signed-in administrator and starts the form with one expense row.</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadRequesterAsync())
        {
            return Forbid();
        }

        SubmissionKey = PaymentRequestSubmissionRules.CreateSubmissionKey();
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
            ModelState.AddModelError("Input.Lines", localizer["Too many expense rows were submitted."]);
        }

        if (Attachments.Count > Limits.MaximumAttachmentCount)
        {
            ModelState.AddModelError(nameof(Attachments), localizer["Attach no more than {0} files.", Limits.MaximumAttachmentCount]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var uploadedFiles = await RequestFormFiles.ReadAsync(
            Attachments, Limits.MaximumAttachmentBytes, ModelState, localizer, HttpContext.RequestAborted);
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
                SubmissionKey,
                HttpContext.RequestAborted);

            TempData["StatusMessage"] = result.IsDuplicate
                ? "This form was already submitted; no duplicate request was created."
                : "Your payment request was submitted for review.";
            return RedirectToPage("Details", new { id = result.RequestId });
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
