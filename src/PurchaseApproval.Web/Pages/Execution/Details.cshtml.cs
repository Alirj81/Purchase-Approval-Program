using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Execution;

[Authorize(Policy = AppPolicies.CanExecutePayments)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DetailsModel(
    ApplicationDbContext dbContext,
    ITransferExecutionService transferService,
    UserManager<ApplicationUser> userManager) : PageModel
{
    public ExecutionReview? Review { get; private set; }

    public bool CanRecordUnsuccessful { get; private set; }

    [BindProperty]
    public OutcomeInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        Input.Version = Review!.TransferVersion;
        return Page();
    }

    public Task<IActionResult> OnPostTransferredAsync(int id)
        => SaveAsync(id, TransferStatus.Transferred);

    public Task<IActionResult> OnPostUnsuccessfulAsync(int id)
        => SaveAsync(id, TransferStatus.Unsuccessful);

    public async Task<IActionResult> OnGetAttachmentAsync(int id, int attachmentId)
    {
        var attachment = await dbContext.PaymentRequestAttachments
            .AsNoTracking()
            .Where(item => item.Id == attachmentId
                && item.PaymentRequestId == id
                && item.PaymentRequest.Status == PaymentRequestStatus.Approved)
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

    private async Task<IActionResult> SaveAsync(int id, TransferStatus status)
    {
        var actor = await userManager.GetUserAsync(User);
        if (actor is null || !actor.IsActive)
        {
            return Forbid();
        }

        var roles = await userManager.GetRolesAsync(actor);
        var isAccountant = roles.Contains(AppRoles.Accountant, StringComparer.Ordinal);
        var isAdministrator = roles.Contains(AppRoles.Administrator, StringComparer.Ordinal);
        if ((!isAccountant && !isAdministrator)
            || (status == TransferStatus.Unsuccessful && !isAccountant))
        {
            return Forbid();
        }

        var actorRole = isAccountant ? AppRoles.Accountant : AppRoles.Administrator;
        if (!ModelState.IsValid)
        {
            return await ReloadAsync(id);
        }

        var outcome = new TransferOutcomeInput(
            status,
            Input.Method,
            Input.OtherMethodDescription,
            Input.DestinationAccountOrCardNumber,
            Input.BankName,
            Input.ChequeNumber,
            Input.ChequeDate,
            Input.ChequePayeeName,
            Input.ReportedLocalDateTime,
            Input.UtcOffsetMinutes,
            Input.FailureReason);

        TransferExecutionResult result;
        try
        {
            result = await transferService.RecordAsync(
                id,
                Input.Version,
                actor.Id,
                actor.FullName,
                actorRole,
                outcome,
                HttpContext.RequestAborted);
        }
        catch (TransferExecutionValidationException exception)
        {
            foreach (var error in exception.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return await ReloadAsync(id);
        }

        if (result == TransferExecutionResult.RequestNotFound)
        {
            return NotFound();
        }
        if (result == TransferExecutionResult.RequestNotApproved)
        {
            return Forbid();
        }
        if (result is TransferExecutionResult.AlreadyRecorded
            or TransferExecutionResult.ConcurrencyConflict)
        {
            ModelState.AddModelError(
                string.Empty,
                "This execution record changed while you were working. The latest outcome is shown below.");
            return await ReloadAsync(id);
        }

        TempData["StatusMessage"] = status == TransferStatus.Transferred
            ? "The externally performed transfer was recorded."
            : "The unsuccessful transfer attempt was recorded and remains open.";
        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> ReloadAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        Input.Version = Review!.TransferVersion;
        return Page();
    }

    private async Task<bool> LoadAsync(int id)
    {
        var actor = await userManager.GetUserAsync(User);
        if (actor is null || !actor.IsActive)
        {
            return false;
        }
        var roles = await userManager.GetRolesAsync(actor);
        CanRecordUnsuccessful = roles.Contains(AppRoles.Accountant, StringComparer.Ordinal);

        var request = await dbContext.PaymentRequests
            .AsNoTracking()
            .Where(item => item.Id == id && item.Status == PaymentRequestStatus.Approved)
            .Select(item => new ExecutionReview(
                item.Id,
                item.ReferenceNumber,
                item.RequesterFullNameSnapshot,
                item.RequesterDepartmentSnapshot,
                item.PayeeName,
                item.RequestedAccountOrCardNumber,
                item.Justification,
                item.TotalAmountRials,
                item.SubmittedAtUtc,
                0,
                item.TransferOutcome == null
                    ? TransferStatus.AwaitingTransfer
                    : item.TransferOutcome.Status,
                item.TransferOutcome != null && item.TransferOutcome.IsClosed,
                item.TransferOutcome == null ? null : item.TransferOutcome.Method,
                item.TransferOutcome == null ? null : item.TransferOutcome.OtherMethodDescription,
                item.TransferOutcome == null ? null : item.TransferOutcome.DestinationAccountOrCardNumber,
                item.TransferOutcome == null ? null : item.TransferOutcome.BankName,
                item.TransferOutcome == null ? null : item.TransferOutcome.ChequeNumber,
                item.TransferOutcome == null ? null : item.TransferOutcome.ChequeDate,
                item.TransferOutcome == null ? null : item.TransferOutcome.ChequePayeeName,
                item.TransferOutcome == null ? null : item.TransferOutcome.PerformedByFullNameSnapshot,
                item.TransferOutcome == null ? null : item.TransferOutcome.PerformedByRoleSnapshot,
                item.TransferOutcome == null ? null : item.TransferOutcome.TransactionOccurredAtUtc,
                item.TransferOutcome == null ? null : item.TransferOutcome.TransactionUtcOffsetMinutes,
                item.TransferOutcome == null ? null : item.TransferOutcome.RecordedAtUtc,
                item.TransferOutcome == null ? null : item.TransferOutcome.FailureReason,
                Array.Empty<RequestLine>(),
                Array.Empty<AttachmentSummary>(),
                Array.Empty<AuditRow>()))
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        if (request is null)
        {
            return false;
        }

        // PostgreSQL's xmin concurrency token cannot participate in a SQL CASE expression.
        // Read it directly from the optional transfer row and use zero for legacy rows with none.
        var transferVersion = await dbContext.TransferOutcomes.AsNoTracking()
            .Where(item => item.PaymentRequestId == id)
            .Select(item => item.Version)
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        var lines = await dbContext.PaymentRequestLines.AsNoTracking()
            .Where(item => item.PaymentRequestId == id)
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new RequestLine(
                item.Description, item.AmountRials, item.HasInvoice, item.Remarks))
            .ToListAsync(HttpContext.RequestAborted);
        var attachments = await dbContext.PaymentRequestAttachments.AsNoTracking()
            .Where(item => item.PaymentRequestId == id)
            .OrderBy(item => item.Id)
            .Select(item => new AttachmentSummary(
                item.Id, item.OriginalFileName, item.FileSizeBytes))
            .ToListAsync(HttpContext.RequestAborted);
        var audits = await dbContext.TransferAuditEntries.AsNoTracking()
            .Where(item => item.TransferOutcome.PaymentRequestId == id)
            .OrderBy(item => item.RecordedAtUtc)
            .Select(item => new AuditRow(
                item.Action,
                item.StatusAfterAction,
                item.ActorFullNameSnapshot,
                item.ActorRoleSnapshot,
                item.TransactionOccurredAtUtc,
                item.RecordedAtUtc))
            .ToListAsync(HttpContext.RequestAborted);

        Review = request with
        {
            TransferVersion = transferVersion,
            Lines = lines,
            Attachments = attachments,
            AuditEntries = audits
        };
        return true;
    }

    public sealed class OutcomeInput
    {
        public uint Version { get; set; }

        [Required]
        public TransferMethod Method { get; set; }

        [StringLength(500)]
        public string? OtherMethodDescription { get; set; }

        [StringLength(100)]
        public string? DestinationAccountOrCardNumber { get; set; }

        [StringLength(200)]
        public string? BankName { get; set; }

        [StringLength(100)]
        public string? ChequeNumber { get; set; }

        public DateOnly? ChequeDate { get; set; }

        [StringLength(200)]
        public string? ChequePayeeName { get; set; }

        [Required]
        public DateTime ReportedLocalDateTime { get; set; }

        [Range(-840, 840)]
        public short UtcOffsetMinutes { get; set; }

        [StringLength(2000)]
        public string? FailureReason { get; set; }
    }

    public sealed record ExecutionReview(
        int Id,
        string ReferenceNumber,
        string RequesterName,
        string? RequesterDepartment,
        string PayeeName,
        string? RequestedDestination,
        string Justification,
        long TotalAmountRials,
        DateTimeOffset SubmittedAtUtc,
        uint TransferVersion,
        TransferStatus TransferStatus,
        bool IsClosed,
        TransferMethod? Method,
        string? OtherMethodDescription,
        string? ActualDestination,
        string? BankName,
        string? ChequeNumber,
        DateOnly? ChequeDate,
        string? ChequePayeeName,
        string? PerformedByName,
        string? PerformedByRole,
        DateTimeOffset? OccurredAtUtc,
        short? UtcOffsetMinutes,
        DateTimeOffset? RecordedAtUtc,
        string? FailureReason,
        IReadOnlyList<RequestLine> Lines,
        IReadOnlyList<AttachmentSummary> Attachments,
        IReadOnlyList<AuditRow> AuditEntries);

    public sealed record RequestLine(
        string Description, long AmountRials, bool HasInvoice, string? Remarks);

    public sealed record AttachmentSummary(int Id, string FileName, long FileSizeBytes);

    public sealed record AuditRow(
        TransferAuditAction Action,
        TransferStatus Status,
        string ActorName,
        string ActorRole,
        DateTimeOffset? OccurredAtUtc,
        DateTimeOffset RecordedAtUtc);
}
