// Shows immutable outcome details, device-entered occurrence time, closure state, and audit history.
// Administrators and accountants may record a Transferred or Unsuccessful outcome for a payment
// they personally attempted; only administrators may close a recorded item early, with a reason.
// The reported time is resolved server-side from the entered date and the device zone; when a
// local time is ambiguous (clocks moved back) the page asks which UTC offset applied.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
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
    UserManager<ApplicationUser> userManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public ExecutionReview? Review { get; private set; }

    public bool CanCloseEarly { get; private set; }

    /// <summary>Offsets offered when the entered local time occurred twice; empty otherwise.</summary>
    public IReadOnlyList<short> AmbiguousOffsetChoices { get; private set; } = [];

    [BindProperty]
    public OutcomeInput Input { get; set; } = new();

    [BindProperty]
    public ClosureInput CloseInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        Input.Version = Review!.TransferVersion;
        CloseInput.Version = Review.TransferVersion;
        return Page();
    }

    public Task<IActionResult> OnPostTransferredAsync(int id)
        => SaveAsync(id, TransferStatus.Transferred);

    public Task<IActionResult> OnPostUnsuccessfulAsync(int id)
        => SaveAsync(id, TransferStatus.Unsuccessful);

    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        // The outcome form's validation attributes must not block the closure form.
        ModelState.Clear();
        var actor = await userManager.GetUserAsync(User);
        if (actor is null || !actor.IsActive
            || !await userManager.IsInRoleAsync(actor, AppRoles.Administrator))
        {
            return Forbid();
        }

        TransferClosureResult result;
        try
        {
            result = await transferService.CloseByAdministratorAsync(
                id,
                CloseInput.Version,
                actor.Id,
                actor.FullName,
                AppRoles.Administrator,
                CloseInput.Reason ?? string.Empty,
                HttpContext.RequestAborted);
        }
        catch (ValidationMessagesException exception)
        {
            foreach (var message in exception.Messages)
            {
                ModelState.AddModelError(string.Empty, localizer[message.Key, message.Args]);
            }
            return await ReloadAsync(id);
        }

        if (result == TransferClosureResult.RequestNotFound)
        {
            return NotFound();
        }
        if (result == TransferClosureResult.OutcomeNotRecorded)
        {
            ModelState.AddModelError(string.Empty, localizer["Record an execution outcome before closing the item."]);
            return await ReloadAsync(id);
        }
        if (result is TransferClosureResult.AlreadyClosed
            or TransferClosureResult.ConcurrencyConflict)
        {
            ModelState.AddModelError(
                string.Empty,
                localizer["This closure state changed while you were working. The latest record is shown below."]);
            return await ReloadAsync(id);
        }

        TempData["StatusMessage"] =
            "The execution item was closed by an administrator. Its outcome and history were preserved.";
        return RedirectToPage(new { id });
    }

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
        var isAdministrator = roles.Contains(AppRoles.Administrator, StringComparer.Ordinal);
        var isAccountant = roles.Contains(AppRoles.Accountant, StringComparer.Ordinal);
        if (!isAccountant && !isAdministrator)
        {
            return Forbid();
        }

        // The authenticated role is the executor's role; a typed name is never accepted.
        var actorRole = isAdministrator ? AppRoles.Administrator : AppRoles.Accountant;
        if (!ModelState.IsValid)
        {
            return await ReloadAsync(id);
        }

        var time = new TransactionTimeInput(Input.ReportedLocalDateTime ?? default, Input.TimeZoneId, Input.ExplicitOffsetMinutes);
        var resolution = TransactionTimeRules.Resolve(time);
        if (resolution.Status == TransactionTimeResolution.AmbiguousLocalTime)
        {
            // Ask the executor which of the two offsets applied instead of guessing.
            AmbiguousOffsetChoices = resolution.OffsetChoices;
            ModelState.AddModelError(string.Empty, localizer[TransactionTimeRules.Describe(resolution).Key]);
            return await ReloadAsync(id, keepInput: true);
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
            time,
            Input.FailureReason,
            Input.ExternalReference);

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
        catch (ValidationMessagesException exception)
        {
            foreach (var message in exception.Messages)
            {
                ModelState.AddModelError(string.Empty, localizer[message.Key, message.Args]);
            }
            return await ReloadAsync(id, keepInput: true);
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
                localizer["This execution record changed while you were working. The latest outcome is shown below."]);
            return await ReloadAsync(id);
        }

        TempData["StatusMessage"] = status == TransferStatus.Transferred
            ? "The transfer was recorded and awaits automatic closure after 24 hours."
            : "The unsuccessful attempt was recorded and awaits automatic closure after 48 hours.";
        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> ReloadAsync(int id, bool keepInput = false)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (!keepInput)
        {
            Input.Version = Review!.TransferVersion;
        }

        CloseInput.Version = Review!.TransferVersion;
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
        var isAdministrator = roles.Contains(AppRoles.Administrator, StringComparer.Ordinal);

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
                item.Decision == null ? null : item.Decision.DecidedAtUtc,
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
                item.TransferOutcome == null ? null : item.TransferOutcome.TransactionTimeZoneId,
                item.TransferOutcome == null ? null : item.TransferOutcome.TransactionEnteredLocalDateTime,
                item.TransferOutcome == null ? null : item.TransferOutcome.RecordedAtUtc,
                item.TransferOutcome == null ? null : item.TransferOutcome.FailureReason,
                item.TransferOutcome == null ? null : item.TransferOutcome.ExternalReference,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosedAtUtc,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosureType,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosedByFullNameSnapshot,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosedByRoleSnapshot,
                item.TransferOutcome == null ? null : item.TransferOutcome.ClosureReason,
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
            .ThenBy(item => item.Id)
            .Select(item => new AuditRow(
                item.Action,
                item.StatusAfterAction,
                item.ActorFullNameSnapshot,
                item.ActorRoleSnapshot,
                item.TransactionOccurredAtUtc,
                item.TransactionUtcOffsetMinutes,
                item.TransactionTimeZoneId,
                item.ClosureReason,
                item.RecordedAtUtc))
            .ToListAsync(HttpContext.RequestAborted);

        Review = request with
        {
            TransferVersion = transferVersion,
            AutomaticClosureDueAtUtc = request.RecordedAtUtc is null
                || request.TransferStatus == TransferStatus.AwaitingTransfer
                || request.IsClosed
                    ? null
                    : request.RecordedAtUtc.Value.Add(
                        request.TransferStatus == TransferStatus.Transferred
                            ? TransferExecutionRules.TransferredClosureDelay
                            : TransferExecutionRules.UnsuccessfulClosureDelay),
            Lines = lines,
            Attachments = attachments,
            AuditEntries = audits
        };
        CanCloseEarly = isAdministrator
            && Review.TransferStatus is TransferStatus.Transferred or TransferStatus.Unsuccessful
            && !Review.IsClosed;
        return true;
    }

    public sealed class OutcomeInput
    {
        public uint Version { get; set; }

        [Required(ErrorMessage = "Choose a valid execution method.")]
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

        // Nullable so an empty field is a validation message, not DateTime.MinValue.
        [Required(ErrorMessage = "Enter when the transfer or attempt occurred.")]
        public DateTime? ReportedLocalDateTime { get; set; }

        /// <summary>IANA zone detected by the device or typed by the executor; may be empty.</summary>
        [StringLength(100)]
        public string? TimeZoneId { get; set; }

        /// <summary>Explicit UTC offset, used when no zone is available or the local time is ambiguous.</summary>
        [Range(-840, 840)]
        public short? ExplicitOffsetMinutes { get; set; }

        [StringLength(2000)]
        public string? FailureReason { get; set; }

        [StringLength(200)]
        public string? ExternalReference { get; set; }
    }

    public sealed class ClosureInput
    {
        public uint Version { get; set; }

        [StringLength(2000)]
        public string? Reason { get; set; }
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
        DateTimeOffset? ApprovedAtUtc,
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
        string? TimeZoneId,
        DateTime? EnteredLocalDateTime,
        DateTimeOffset? RecordedAtUtc,
        string? FailureReason,
        string? ExternalReference,
        DateTimeOffset? ClosedAtUtc,
        TransferClosureType? ClosureType,
        string? ClosedByName,
        string? ClosedByRole,
        string? ClosureReason,
        IReadOnlyList<RequestLine> Lines,
        IReadOnlyList<AttachmentSummary> Attachments,
        IReadOnlyList<AuditRow> AuditEntries)
    {
        public DateTimeOffset? AutomaticClosureDueAtUtc { get; init; }
    }

    public sealed record RequestLine(
        string Description, long AmountRials, bool HasInvoice, string? Remarks);

    public sealed record AttachmentSummary(int Id, string FileName, long FileSizeBytes);

    public sealed record AuditRow(
        TransferAuditAction Action,
        TransferStatus Status,
        string ActorName,
        string ActorRole,
        DateTimeOffset? OccurredAtUtc,
        short? UtcOffsetMinutes,
        string? TimeZoneId,
        string? ClosureReason,
        DateTimeOffset RecordedAtUtc);
}
