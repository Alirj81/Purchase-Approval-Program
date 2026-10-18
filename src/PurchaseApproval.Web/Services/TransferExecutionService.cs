using Microsoft.EntityFrameworkCore;
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
}

public enum TransferExecutionResult
{
    Success,
    RequestNotFound,
    RequestNotApproved,
    AlreadyRecorded,
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
    DateTime ReportedLocalDateTime,
    short UtcOffsetMinutes,
    string? FailureReason);

public sealed class TransferExecutionValidationException(IReadOnlyList<string> errors)
    : Exception("The transfer outcome is invalid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class TransferExecutionService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : ITransferExecutionService
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

        TransferExecutionRules.ApplyOutcome(
            transfer, actorUserId, actorFullName, actorRole, input, recordedAtUtc);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return TransferExecutionResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransferExecutionResult.ConcurrencyConflict;
        }
        catch (DbUpdateException)
        {
            // The one-outcome-per-request index also protects concurrent first recordings.
            return TransferExecutionResult.ConcurrencyConflict;
        }
    }
}

public static class TransferExecutionRules
{
    public const int MaximumTextLength = 2000;

    public static TransferOutcome CreateAwaitingTransfer(
        PaymentRequest request,
        int actorUserId,
        string actorFullName,
        string actorRole,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actorUserId, actorFullName, actorRole, allowAdministrator: true);

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

    public static TransferAuditEntry ApplyOutcome(
        TransferOutcome transfer,
        int actorUserId,
        string actorFullName,
        string actorRole,
        TransferOutcomeInput input,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(input);

        var allowAdministrator = input.Status == TransferStatus.Transferred;
        ValidateActor(actorUserId, actorFullName, actorRole, allowAdministrator);

        var errors = new List<string>();
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

        if (input.UtcOffsetMinutes is < -840 or > 840)
        {
            errors.Add("The reported timezone offset is invalid.");
        }

        if (input.ReportedLocalDateTime == default)
        {
            errors.Add("Enter when the transfer or attempt occurred.");
        }

        var otherMethod = OptionalText(input.OtherMethodDescription, 500,
            "Other method description", errors);
        var destination = OptionalText(input.DestinationAccountOrCardNumber, 100,
            "Destination account or card number", errors);
        var bankName = OptionalText(input.BankName, 200, "Bank name", errors);
        var chequeNumber = OptionalText(input.ChequeNumber, 100, "Cheque number", errors);
        var chequePayee = OptionalText(input.ChequePayeeName, 200, "Cheque payee", errors);
        var failureReason = OptionalText(input.FailureReason, MaximumTextLength,
            "Failure explanation", errors);

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

        var local = DateTime.SpecifyKind(input.ReportedLocalDateTime, DateTimeKind.Unspecified);
        var occurrence = new DateTimeOffset(
            local, TimeSpan.FromMinutes(input.UtcOffsetMinutes)).ToUniversalTime();
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
        transfer.TransactionOccurredAtUtc = occurrence;
        transfer.TransactionUtcOffsetMinutes = input.UtcOffsetMinutes;
        transfer.RecordedAtUtc = recordedAtUtc;
        transfer.FailureReason = input.Status == TransferStatus.Unsuccessful ? failureReason : null;
        transfer.ExternalReference = null;
        transfer.IsClosed = input.Status == TransferStatus.Transferred;

        var audit = new TransferAuditEntry
        {
            TransferOutcome = transfer,
            Action = input.Status == TransferStatus.Transferred
                ? TransferAuditAction.MarkedTransferred
                : TransferAuditAction.MarkedUnsuccessful,
            StatusAfterAction = input.Status,
            IsClosedAfterAction = transfer.IsClosed,
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
            TransactionOccurredAtUtc = occurrence,
            TransactionUtcOffsetMinutes = input.UtcOffsetMinutes,
            FailureReason = transfer.FailureReason,
            RecordedAtUtc = recordedAtUtc
        };
        transfer.AuditEntries.Add(audit);
        return audit;
    }

    private static void ValidateActor(
        int actorUserId,
        string actorFullName,
        string actorRole,
        bool allowAdministrator)
    {
        var validRole = actorRole == AppRoles.Accountant
            || (allowAdministrator && actorRole == AppRoles.Administrator);
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
        ICollection<string> errors)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean?.Length > maximumLength)
        {
            errors.Add($"{label} cannot exceed {maximumLength:N0} characters.");
        }

        return clean;
    }
}
