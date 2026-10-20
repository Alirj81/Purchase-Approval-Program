// Records one external execution outcome and supports separate audited administrator closure.
// Automatic 24/48-hour closure is handled by TransferClosureProcessor and its hosted worker.
//
// Responsibilities split: TransferExecutionService loads/saves and maps database failures;
// TransferExecutionRules holds the pure business rules (who may record what, which method needs
// which details, time bounds via TransactionTimeRules, closure reasons) so they unit-test fast.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Services;

public interface ITransferExecutionService
{
    Task<TransferExecutionResult> RecordAsync(
        int requestId,
        uint expectedVersion,
        int actorUserId,
        string actorFullName,
        string actorRole,
        TransferOutcomeInput input,
        CancellationToken cancellationToken = default);

    Task<TransferClosureResult> CloseByAdministratorAsync(
        int requestId,
        uint expectedVersion,
        int actorUserId,
        string actorFullName,
        string actorRole,
        string closureReason,
        CancellationToken cancellationToken = default);
}

public enum TransferExecutionResult
{
    Success,
    RequestNotFound,
    RequestNotApproved,
    AlreadyRecorded,
    ConcurrencyConflict
}

public enum TransferClosureResult
{
    Success,
    RequestNotFound,
    OutcomeNotRecorded,
    AlreadyClosed,
    ConcurrencyConflict
}

public sealed record TransferOutcomeInput(
    TransferStatus Status,
    TransferMethod Method,
    string? OtherMethodDescription,
    string? DestinationAccountOrCardNumber,
    string? BankName,
    string? ChequeNumber,
    DateOnly? ChequeDate,
    string? ChequePayeeName,
    TransactionTimeInput Time,
    string? FailureReason,
    string? ExternalReference);

/// <summary>Bounds applied to the reported occurrence time; see TransferExecutionRules.ApplyOutcome.</summary>
public sealed record TransferTimePolicy(DateTimeOffset? ApprovedAtUtc, TimeSpan FutureTolerance)
{
    public static readonly TransferTimePolicy Unbounded = new(null, TimeSpan.FromDays(36500));
}

public sealed class TransferExecutionValidationException(IReadOnlyList<LocalizableMessage> messages)
    : ValidationMessagesException("The transfer outcome is invalid.", messages);

public sealed class TransferExecutionService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<ApplicationLimitsOptions> limits) : ITransferExecutionService
{
    public async Task<TransferExecutionResult> RecordAsync(
        int requestId,
        uint expectedVersion,
        int actorUserId,
        string actorFullName,
        string actorRole,
        TransferOutcomeInput input,
        CancellationToken cancellationToken = default)
    {
        var request = await dbContext.PaymentRequests
            .Include(item => item.TransferOutcome)
            .Include(item => item.Decision)
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);

        if (request is null)
        {
            return TransferExecutionResult.RequestNotFound;
        }

        if (request.Status != PaymentRequestStatus.Approved)
        {
            return TransferExecutionResult.RequestNotApproved;
        }

        var recordedAtUtc = timeProvider.GetUtcNow();
        var transfer = request.TransferOutcome;
        if (transfer is null)
        {
            if (expectedVersion != 0)
            {
                return TransferExecutionResult.ConcurrencyConflict;
            }

            transfer = TransferExecutionRules.CreateAwaitingTransfer(
                request, actorUserId, actorFullName, actorRole, recordedAtUtc);
            dbContext.TransferOutcomes.Add(transfer);
        }
        else
        {
            if (transfer.Status != TransferStatus.AwaitingTransfer)
            {
                return TransferExecutionResult.AlreadyRecorded;
            }

            if (transfer.Version != expectedVersion)
            {
                return TransferExecutionResult.ConcurrencyConflict;
            }

            dbContext.Entry(transfer).Property(item => item.Version).OriginalValue = expectedVersion;
        }

        var policy = new TransferTimePolicy(
            request.Decision?.DecidedAtUtc,
            TimeSpan.FromMinutes(limits.Value.TransactionTimeFutureToleranceMinutes));
        TransferExecutionRules.ApplyOutcome(
            transfer, actorUserId, actorFullName, actorRole, input, recordedAtUtc, policy);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return TransferExecutionResult.Success;
        }
        catch (DbUpdateException exception)
        {
            // Only a genuine concurrency failure or the one-outcome-per-request index becomes a
            // conflict result; constraint violations surface as validation and anything else
            // propagates to the global error handler, which logs a correlation ID.
            var failure = DatabaseErrorClassifier.Classify(exception);
            switch (failure.Kind)
            {
                case DatabaseFailureKind.ConcurrencyConflict:
                    return TransferExecutionResult.ConcurrencyConflict;
                case DatabaseFailureKind.UniqueViolation:
                    return TransferExecutionResult.AlreadyRecorded;
                case DatabaseFailureKind.ConstraintViolation:
                    throw new TransferExecutionValidationException(
                        [DatabaseErrorClassifier.DescribeConstraintViolation()]);
                default:
                    throw;
            }
        }
    }

    public async Task<TransferClosureResult> CloseByAdministratorAsync(
        int requestId,
        uint expectedVersion,
        int actorUserId,
        string actorFullName,
        string actorRole,
        string closureReason,
        CancellationToken cancellationToken = default)
    {
        var transfer = await dbContext.TransferOutcomes
            .Include(item => item.PaymentRequest)
            .SingleOrDefaultAsync(
                item => item.PaymentRequestId == requestId,
                cancellationToken);

        if (transfer is null)
        {
            return TransferClosureResult.RequestNotFound;
        }

        if (transfer.Status == TransferStatus.AwaitingTransfer)
        {
            return TransferClosureResult.OutcomeNotRecorded;
        }

        if (transfer.IsClosed)
        {
            return TransferClosureResult.AlreadyClosed;
        }

        if (transfer.Version != expectedVersion)
        {
            return TransferClosureResult.ConcurrencyConflict;
        }

        dbContext.Entry(transfer).Property(item => item.Version).OriginalValue = expectedVersion;
        TransferExecutionRules.ApplyAdministratorClosure(
            transfer,
            actorUserId,
            actorFullName,
            actorRole,
            timeProvider.GetUtcNow(),
            closureReason);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return TransferClosureResult.Success;
        }
        catch (DbUpdateException exception)
        {
            var failure = DatabaseErrorClassifier.Classify(exception);
            switch (failure.Kind)
            {
                case DatabaseFailureKind.ConcurrencyConflict:
                case DatabaseFailureKind.UniqueViolation:
                    return TransferClosureResult.ConcurrencyConflict;
                case DatabaseFailureKind.ConstraintViolation:
                    throw new TransferExecutionValidationException(
                        [DatabaseErrorClassifier.DescribeConstraintViolation()]);
                default:
                    throw;
            }
        }
    }

}

public static class TransferExecutionRules
{
    public const int MaximumTextLength = 2000;
    public static readonly TimeSpan TransferredClosureDelay = TimeSpan.FromHours(24);
    public static readonly TimeSpan UnsuccessfulClosureDelay = TimeSpan.FromHours(48);

    public const string AutomaticTransferredClosureReason =
        "Automatic closure 24 hours after the transferred outcome was recorded.";

    public const string AutomaticUnsuccessfulClosureReason =
        "Automatic closure 48 hours after the unsuccessful outcome was recorded.";

    public static TransferOutcome CreateAwaitingTransfer(
        PaymentRequest request,
        int actorUserId,
        string actorFullName,
        string actorRole,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actorUserId, actorFullName, actorRole);

        if (request.Status != PaymentRequestStatus.Approved)
        {
            throw new TransferExecutionValidationException(
                ["Only an approved request can enter the transfer queue."]);
        }

        var cleanName = actorFullName.Trim();
        var transfer = new TransferOutcome
        {
            PaymentRequest = request,
            Status = TransferStatus.AwaitingTransfer,
            IsClosed = false
        };
        transfer.AuditEntries.Add(new TransferAuditEntry
        {
            TransferOutcome = transfer,
            Action = TransferAuditAction.AwaitingTransferCreated,
            StatusAfterAction = TransferStatus.AwaitingTransfer,
            IsClosedAfterAction = false,
            ActorUserId = actorUserId,
            ActorFullNameSnapshot = cleanName,
            ActorRoleSnapshot = actorRole,
            RecordedAtUtc = recordedAtUtc
        });
        request.TransferOutcome = transfer;
        return transfer;
    }

    /// <summary>
    /// Applies a Transferred or Unsuccessful outcome recorded by the authenticated executor.
    /// Administrators and accountants may record either outcome for a payment they personally
    /// attempted; an unsuccessful outcome always needs a reason. The reported occurrence is
    /// resolved through TransactionTimeRules and must fall between approval and the server
    /// recording time plus the configured clock-skew tolerance.
    /// </summary>
    public static TransferAuditEntry ApplyOutcome(
        TransferOutcome transfer,
        int actorUserId,
        string actorFullName,
        string actorRole,
        TransferOutcomeInput input,
        DateTimeOffset recordedAtUtc,
        TransferTimePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(input);
        policy ??= TransferTimePolicy.Unbounded;

        ValidateActor(actorUserId, actorFullName, actorRole);

        var errors = new List<LocalizableMessage>();
        if (transfer.Status != TransferStatus.AwaitingTransfer)
        {
            errors.Add("This request already has a transfer outcome.");
        }

        if (input.Status is not (TransferStatus.Transferred or TransferStatus.Unsuccessful))
        {
            errors.Add("Choose Transferred or Unsuccessful.");
        }

        if (!Enum.IsDefined(input.Method))
        {
            errors.Add("Choose a valid execution method.");
        }

        var time = ResolveTime(input.Time, recordedAtUtc, policy, errors);

        var otherMethod = OptionalText(input.OtherMethodDescription, 500,
            "Other method description", errors);
        var destination = OptionalText(input.DestinationAccountOrCardNumber, 100,
            "Destination account or card number", errors);
        var bankName = OptionalText(input.BankName, 200, "Bank name", errors);
        var chequeNumber = OptionalText(input.ChequeNumber, 100, "Cheque number", errors);
        var chequePayee = OptionalText(input.ChequePayeeName, 200, "Cheque payee", errors);
        var failureReason = OptionalText(input.FailureReason, MaximumTextLength,
            "Failure explanation", errors);
        var externalReference = OptionalText(input.ExternalReference, 200,
            "External accounting reference", errors);

        if (input.Status == TransferStatus.Unsuccessful && failureReason is null)
        {
            errors.Add("Enter a reason for the unsuccessful payment attempt.");
        }

        if (input.Method == TransferMethod.BankTransfer && destination is null)
        {
            errors.Add("Enter the actual destination account or card number for a bank transfer.");
        }

        if (input.Method == TransferMethod.Cheque
            && (chequeNumber is null || input.ChequeDate is null
                || bankName is null || chequePayee is null))
        {
            errors.Add("Cheque number, cheque date, bank, and payee are required for a cheque.");
        }

        if (input.Method == TransferMethod.Other && otherMethod is null)
        {
            errors.Add("Describe the execution method when Other is selected.");
        }

        if (errors.Count != 0)
        {
            throw new TransferExecutionValidationException(errors);
        }

        var cleanName = actorFullName.Trim();

        transfer.Status = input.Status;
        transfer.Method = input.Method;
        transfer.OtherMethodDescription = input.Method == TransferMethod.Other ? otherMethod : null;
        transfer.DestinationAccountOrCardNumber = input.Method == TransferMethod.BankTransfer
            ? destination : null;
        transfer.BankName = input.Method is TransferMethod.BankTransfer or TransferMethod.Cheque
            ? bankName : null;
        transfer.ChequeNumber = input.Method == TransferMethod.Cheque ? chequeNumber : null;
        transfer.ChequeDate = input.Method == TransferMethod.Cheque ? input.ChequeDate : null;
        transfer.ChequePayeeName = input.Method == TransferMethod.Cheque ? chequePayee : null;
        transfer.PerformedByUserId = actorUserId;
        transfer.PerformedByFullNameSnapshot = cleanName;
        transfer.PerformedByRoleSnapshot = actorRole;
        transfer.TransactionOccurredAtUtc = time!.OccurrenceUtc;
        transfer.TransactionUtcOffsetMinutes = time.OffsetMinutes;
        transfer.TransactionTimeZoneId = time.TimeZoneId;
        transfer.TransactionEnteredLocalDateTime = time.EnteredLocalDateTime;
        transfer.RecordedAtUtc = recordedAtUtc;
        transfer.FailureReason = input.Status == TransferStatus.Unsuccessful ? failureReason : null;
        transfer.ExternalReference = externalReference;
        transfer.IsClosed = false;
        transfer.ClosedAtUtc = null;
        transfer.ClosureType = null;
        transfer.ClosedByUserId = null;
        transfer.ClosedByFullNameSnapshot = null;
        transfer.ClosedByRoleSnapshot = null;
        transfer.ClosureReason = null;

        var audit = new TransferAuditEntry
        {
            TransferOutcome = transfer,
            Action = input.Status == TransferStatus.Transferred
                ? TransferAuditAction.MarkedTransferred
                : TransferAuditAction.MarkedUnsuccessful,
            StatusAfterAction = input.Status,
            IsClosedAfterAction = false,
            Method = transfer.Method,
            OtherMethodDescription = transfer.OtherMethodDescription,
            DestinationAccountOrCardNumber = transfer.DestinationAccountOrCardNumber,
            BankName = transfer.BankName,
            ChequeNumber = transfer.ChequeNumber,
            ChequeDate = transfer.ChequeDate,
            ChequePayeeName = transfer.ChequePayeeName,
            ActorUserId = actorUserId,
            ActorFullNameSnapshot = cleanName,
            ActorRoleSnapshot = actorRole,
            TransactionOccurredAtUtc = transfer.TransactionOccurredAtUtc,
            TransactionUtcOffsetMinutes = transfer.TransactionUtcOffsetMinutes,
            TransactionTimeZoneId = transfer.TransactionTimeZoneId,
            TransactionEnteredLocalDateTime = transfer.TransactionEnteredLocalDateTime,
            FailureReason = transfer.FailureReason,
            ExternalReference = transfer.ExternalReference,
            RecordedAtUtc = recordedAtUtc
        };
        transfer.AuditEntries.Add(audit);
        return audit;
    }

    public static DateTimeOffset GetAutomaticClosureDueAt(TransferOutcome transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (transfer.RecordedAtUtc is null
            || transfer.Status is not (TransferStatus.Transferred or TransferStatus.Unsuccessful))
        {
            throw new TransferExecutionValidationException(
                ["Only a recorded transferred or unsuccessful outcome has a closure timer."]);
        }

        var delay = transfer.Status == TransferStatus.Transferred
            ? TransferredClosureDelay
            : UnsuccessfulClosureDelay;
        return transfer.RecordedAtUtc.Value.Add(delay);
    }

    public static TransferAuditEntry ApplyAutomaticClosure(
        TransferOutcome transfer,
        DateTimeOffset closedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ValidateClosable(transfer);

        var dueAtUtc = GetAutomaticClosureDueAt(transfer);
        if (closedAtUtc < dueAtUtc)
        {
            throw new TransferExecutionValidationException(
                ["The automatic closure timer has not elapsed."]);
        }

        transfer.IsClosed = true;
        transfer.ClosedAtUtc = closedAtUtc;
        transfer.ClosureType = TransferClosureType.Automatic;
        transfer.ClosedByUserId = null;
        transfer.ClosedByFullNameSnapshot = null;
        transfer.ClosedByRoleSnapshot = null;
        transfer.ClosureReason = transfer.Status == TransferStatus.Transferred
            ? AutomaticTransferredClosureReason
            : AutomaticUnsuccessfulClosureReason;

        var action = transfer.Status == TransferStatus.Transferred
            ? TransferAuditAction.TransferredClosedAutomatically
            : TransferAuditAction.UnsuccessfulClosedAutomatically;
        var audit = CreateClosureAudit(
            transfer,
            action,
            actorUserId: null,
            actorName: "Automatic closure service",
            actorRole: "System",
            closedAtUtc);
        transfer.AuditEntries.Add(audit);
        return audit;
    }

    /// <summary>
    /// Closes an outcome-recorded item before its timer. The reason is mandatory because early
    /// closure is a deliberate decision that later readers must be able to understand.
    /// </summary>
    public static TransferAuditEntry ApplyAdministratorClosure(
        TransferOutcome transfer,
        int actorUserId,
        string actorFullName,
        string actorRole,
        DateTimeOffset closedAtUtc,
        string? closureReason)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (actorRole != AppRoles.Administrator)
        {
            throw new TransferExecutionValidationException(
                ["Only an authenticated administrator may close an execution item early."]);
        }

        ValidateActor(actorUserId, actorFullName, actorRole);
        ValidateClosable(transfer);

        var errors = new List<LocalizableMessage>();
        var cleanReason = OptionalText(closureReason, MaximumTextLength, "Closure reason", errors);
        if (cleanReason is null)
        {
            errors.Add("Enter the reason for closing this execution item early.");
        }

        if (transfer.RecordedAtUtc is { } recordedAt && closedAtUtc < recordedAt)
        {
            errors.Add("Closure cannot be recorded before the execution outcome.");
        }

        if (errors.Count != 0)
        {
            throw new TransferExecutionValidationException(errors);
        }

        var cleanName = actorFullName.Trim();
        transfer.IsClosed = true;
        transfer.ClosedAtUtc = closedAtUtc;
        transfer.ClosureType = TransferClosureType.Administrator;
        transfer.ClosedByUserId = actorUserId;
        transfer.ClosedByFullNameSnapshot = cleanName;
        transfer.ClosedByRoleSnapshot = actorRole;
        transfer.ClosureReason = cleanReason;

        var audit = CreateClosureAudit(
            transfer,
            TransferAuditAction.ClosedByAdministrator,
            actorUserId,
            cleanName,
            actorRole,
            closedAtUtc);
        transfer.AuditEntries.Add(audit);
        return audit;
    }

    private static TransactionTimeResult? ResolveTime(
        TransactionTimeInput? input,
        DateTimeOffset recordedAtUtc,
        TransferTimePolicy policy,
        ICollection<LocalizableMessage> errors)
    {
        if (input is null)
        {
            errors.Add("Enter when the transfer or attempt occurred.");
            return null;
        }

        var result = TransactionTimeRules.Resolve(input);
        if (!result.IsResolved)
        {
            errors.Add(TransactionTimeRules.Describe(result));
            return result;
        }

        var occurrence = result.OccurrenceUtc!.Value;
        if (occurrence > recordedAtUtc.Add(policy.FutureTolerance))
        {
            errors.Add(new LocalizableMessage(
                "The reported transaction time is later than the server time plus the {0}-minute tolerance. Check the device clock and timezone.",
                (int)policy.FutureTolerance.TotalMinutes));
        }

        // Entered times have minute precision while approval timestamps carry seconds, so the
        // comparison floors the approval to its minute; otherwise "approved 10:05:30, paid 10:05"
        // would be rejected although it is the same minute.
        if (policy.ApprovedAtUtc is { } approvedAt)
        {
            var approvalMinute = approvedAt.ToUniversalTime();
            approvalMinute = approvalMinute.AddSeconds(-approvalMinute.Second).AddTicks(-(approvalMinute.Ticks % TimeSpan.TicksPerSecond));
            if (occurrence < approvalMinute)
            {
                errors.Add("The reported transaction time is earlier than the approval. A payment cannot be attempted before it is authorized.");
            }
        }

        return result;
    }

    private static void ValidateClosable(TransferOutcome transfer)
    {
        var errors = new List<LocalizableMessage>();
        if (transfer.Status is not (TransferStatus.Transferred or TransferStatus.Unsuccessful))
        {
            errors.Add("Record a transferred or unsuccessful outcome before closing the item.");
        }

        if (transfer.IsClosed)
        {
            errors.Add("This execution item is already closed.");
        }

        if (errors.Count != 0)
        {
            throw new TransferExecutionValidationException(errors);
        }
    }

    private static TransferAuditEntry CreateClosureAudit(
        TransferOutcome transfer,
        TransferAuditAction action,
        int? actorUserId,
        string actorName,
        string actorRole,
        DateTimeOffset recordedAtUtc) => new()
    {
        TransferOutcome = transfer,
        Action = action,
        StatusAfterAction = transfer.Status,
        IsClosedAfterAction = true,
        Method = transfer.Method,
        OtherMethodDescription = transfer.OtherMethodDescription,
        DestinationAccountOrCardNumber = transfer.DestinationAccountOrCardNumber,
        BankName = transfer.BankName,
        ChequeNumber = transfer.ChequeNumber,
        ChequeDate = transfer.ChequeDate,
        ChequePayeeName = transfer.ChequePayeeName,
        ActorUserId = actorUserId,
        ActorFullNameSnapshot = actorName,
        ActorRoleSnapshot = actorRole,
        TransactionOccurredAtUtc = transfer.TransactionOccurredAtUtc,
        TransactionUtcOffsetMinutes = transfer.TransactionUtcOffsetMinutes,
        TransactionTimeZoneId = transfer.TransactionTimeZoneId,
        TransactionEnteredLocalDateTime = transfer.TransactionEnteredLocalDateTime,
        FailureReason = transfer.FailureReason,
        ExternalReference = transfer.ExternalReference,
        ClosureReason = transfer.ClosureReason,
        RecordedAtUtc = recordedAtUtc
    };

    // Both privileged roles may record the outcome of a payment they personally attempted.
    // The authenticated identity is the executor; a typed name is never accepted.
    private static void ValidateActor(int actorUserId, string actorFullName, string actorRole)
    {
        var validRole = actorRole is AppRoles.Accountant or AppRoles.Administrator;
        if (actorUserId <= 0 || string.IsNullOrWhiteSpace(actorFullName)
            || actorFullName.Trim().Length > 200 || !validRole)
        {
            throw new TransferExecutionValidationException(
                ["The authenticated account is not allowed to record this outcome."]);
        }
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string label,
        ICollection<LocalizableMessage> errors)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean?.Length > maximumLength)
        {
            errors.Add(new LocalizableMessage("{0} cannot exceed {1} characters.", label, maximumLength));
        }

        return clean;
    }
}
