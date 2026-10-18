// This file loads one complete request for administrators and handles its final decision.
using Microsoft.AspNetCore.Authorization;
// Identity resolves the signed-in administrator whose identity is snapshotted with the decision.
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
// Entity Framework Core loads only the attachment bytes requested for an authorized download.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Requests;

/// <summary>Displays request evidence and records an approval or rejection.</summary>
[Authorize(Policy = AppPolicies.CanDecideRequests)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DetailsModel(
    ApplicationDbContext dbContext,
    IApprovalDecisionService decisionService,
    UserManager<ApplicationUser> userManager) : PageModel
{
    public RequestReview? Review { get; private set; }

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    /// <summary>Loads the current request details and evidence metadata.</summary>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadRequestAsync(id))
        {
            return NotFound();
        }

        Input.Version = Review!.Version;
        return Page();
    }

    /// <summary>Approves a pending request and discards any rejection-only text.</summary>
    public Task<IActionResult> OnPostApproveAsync(int id) =>
        SaveDecisionAsync(id, ApprovalDecisionType.Approved);

    /// <summary>Rejects a pending request when the administrator supplies a reason.</summary>
    public Task<IActionResult> OnPostRejectAsync(int id) =>
        SaveDecisionAsync(id, ApprovalDecisionType.Rejected);

    /// <summary>Downloads one attachment only after confirming that it belongs to this request.</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(int id, int attachmentId)
    {
        var attachment = await dbContext.PaymentRequestAttachments
            .AsNoTracking()
            .Where(item => item.Id == attachmentId && item.PaymentRequestId == id)
            .Select(item => new
            {
                item.Content,
                item.ContentType,
                item.OriginalFileName
            })
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        return attachment is null
            ? NotFound()
            : File(attachment.Content, attachment.ContentType, attachment.OriginalFileName);
    }

    /// <summary>Validates the actor and maps service outcomes to safe page responses.</summary>
    private async Task<IActionResult> SaveDecisionAsync(
        int id,
        ApprovalDecisionType decisionType)
    {
        if (decisionType == ApprovalDecisionType.Approved && !Input.ApprovalConfirmed)
        {
            ModelState.AddModelError(
                string.Empty,
                "Confirm the saved reference, amount, and currency before approving.");
            return await ReloadPageAsync(id);
        }

        var administrator = await userManager.GetUserAsync(User);
        if (administrator is null || !administrator.IsActive)
        {
            return Forbid();
        }

        ApprovalDecisionResult result;
        try
        {
            result = await decisionService.DecideAsync(
                id,
                Input.Version,
                administrator.Id,
                administrator.FullName,
                decisionType,
                Input.RejectionReason,
                HttpContext.RequestAborted);
        }
        catch (ApprovalDecisionValidationException exception)
        {
            foreach (var error in exception.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return await ReloadPageAsync(id);
        }

        if (result == ApprovalDecisionResult.RequestNotFound)
        {
            return NotFound();
        }

        if (result is ApprovalDecisionResult.AlreadyDecided
            or ApprovalDecisionResult.ConcurrencyConflict)
        {
            ModelState.AddModelError(
                string.Empty,
                "This request changed while you were reviewing it. The latest decision is shown below.");
            return await ReloadPageAsync(id);
        }

        TempData["StatusMessage"] = decisionType == ApprovalDecisionType.Approved
            ? "The payment request was approved."
            : "The payment request was rejected.";
        return RedirectToPage(new { id });
    }

    /// <summary>Reloads the request after validation or concurrency feedback.</summary>
    private async Task<IActionResult> ReloadPageAsync(int id)
    {
        if (!await LoadRequestAsync(id))
        {
            return NotFound();
        }

        Input.Version = Review!.Version;
        return Page();
    }

    /// <summary>Loads display fields without pulling stored attachment content into the review page.</summary>
    private async Task<bool> LoadRequestAsync(int id)
    {
        var request = await dbContext.PaymentRequests
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new RequestReview(
                item.Id,
                item.ReferenceNumber,
                item.Status,
                item.Version,
                item.RequesterFullNameSnapshot,
                item.RequesterUsernameSnapshot,
                item.RequesterPhoneNumberSnapshot,
                item.RequesterDepartmentSnapshot,
                item.RequesterBranchSnapshot,
                item.RequesterJobTitleSnapshot,
                item.PayeeName,
                item.RequestedAccountOrCardNumber,
                item.Justification,
                item.TotalAmountRials,
                item.SubmittedAtUtc,
                item.Decision == null
                    ? null
                    : new DecisionSummary(
                        item.Decision.DecisionType,
                        item.Decision.DecidedByFullNameSnapshot,
                        item.Decision.RejectionReason,
                        item.Decision.DecidedAtUtc),
                Array.Empty<RequestLine>(),
                Array.Empty<AttachmentSummary>()))
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        if (request is null)
        {
            return false;
        }

        var lines = await dbContext.PaymentRequestLines
            .AsNoTracking()
            .Where(item => item.PaymentRequestId == id)
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new RequestLine(
                item.Description,
                item.AmountRials,
                item.HasInvoice,
                item.Remarks))
            .ToListAsync(HttpContext.RequestAborted);

        var attachments = await dbContext.PaymentRequestAttachments
            .AsNoTracking()
            .Where(item => item.PaymentRequestId == id)
            .OrderBy(item => item.Id)
            .Select(item => new AttachmentSummary(
                item.Id,
                item.OriginalFileName,
                item.ContentType,
                item.FileSizeBytes,
                item.UploadedAtUtc))
            .ToListAsync(HttpContext.RequestAborted);

        Review = request with { Lines = lines, Attachments = attachments };
        return true;
    }

    /// <summary>Fields posted by the two decision forms.</summary>
    public sealed class DecisionInput
    {
        public uint Version { get; set; }

        public bool ApprovalConfirmed { get; set; }

        public string? RejectionReason { get; set; }
    }

    /// <summary>Safe display model for the request and its decision.</summary>
    public sealed record RequestReview(
        int Id,
        string ReferenceNumber,
        PaymentRequestStatus Status,
        uint Version,
        string RequesterName,
        string RequesterUsername,
        string? RequesterPhone,
        string? Department,
        string? Branch,
        string? JobTitle,
        string PayeeName,
        string? RequestedAccountOrCardNumber,
        string Justification,
        long TotalAmountRials,
        DateTimeOffset SubmittedAtUtc,
        DecisionSummary? Decision,
        IReadOnlyList<RequestLine> Lines,
        IReadOnlyList<AttachmentSummary> Attachments);

    public sealed record RequestLine(
        string Description,
        long AmountRials,
        bool HasInvoice,
        string? Remarks);

    public sealed record AttachmentSummary(
        int Id,
        string FileName,
        string ContentType,
        long FileSizeBytes,
        DateTimeOffset UploadedAtUtc);

    public sealed record DecisionSummary(
        ApprovalDecisionType DecisionType,
        string AdministratorName,
        string? RejectionReason,
        DateTimeOffset DecidedAtUtc);
}
