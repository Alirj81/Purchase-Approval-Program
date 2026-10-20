// Stores one immutable external execution outcome plus a separate operational closure state.
// Closure never changes the outcome and never deletes the request, execution data, or audit trail.
//
// Time handling: the executor reports when the payment happened (TransactionEnteredLocalDateTime
// in TransactionTimeZoneId, resolved to TransactionOccurredAtUtc with TransactionUtcOffsetMinutes
// at that instant). RecordedAtUtc is the server clock when the row was saved and is never edited.
namespace PurchaseApproval.Models;

public class TransferOutcome
{
    public int Id { get; set; }

    public int PaymentRequestId { get; set; }

    public PaymentRequest PaymentRequest { get; set; }
        = null!;

    public TransferStatus Status { get; set; }
        = TransferStatus.AwaitingTransfer;

    public TransferMethod? Method { get; set; }
    public string? OtherMethodDescription { get; set; }

    // Actual destination used for a bank transfer
    public string? DestinationAccountOrCardNumber { get; set; }

    public string? BankName { get; set; }

    // Cheque-specific information
    public string? ChequeNumber { get; set; }

    public DateOnly? ChequeDate { get; set; }

    public string? ChequePayeeName { get; set; }

    // The authenticated accountant or administrator
    // who performed and confirmed the transaction
    public int? PerformedByUserId { get; set; }

    public ApplicationUser? PerformedByUser { get; set; }

    public string? PerformedByFullNameSnapshot { get; set; }

    public string? PerformedByRoleSnapshot { get; set; }

    // The instant the user reports that the transaction happened (UTC)
    public DateTimeOffset? TransactionOccurredAtUtc { get; set; }

    // The offset that applied at the reported occurrence, resolved from the entered zone
    public short? TransactionUtcOffsetMinutes { get; set; }

    // IANA zone the executor's device reported (for example Europe/Berlin); null when the
    // device could not supply one and the executor chose an explicit offset instead.
    public string? TransactionTimeZoneId { get; set; }

    // The wall-clock date/time exactly as entered, kept so the entry can be re-checked later.
    public DateTime? TransactionEnteredLocalDateTime { get; set; }

    // The time our server received and saved the result
    public DateTimeOffset? RecordedAtUtc { get; set; }

    public string? FailureReason { get; set; }

    // Optional neutral reference from any accounting or external system.
    public string? ExternalReference { get; set; }

    // Closure is operational state only; it is not payment status or data retention.
    public bool IsClosed { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    public TransferClosureType? ClosureType { get; set; }

    public int? ClosedByUserId { get; set; }

    public ApplicationUser? ClosedByUser { get; set; }

    public string? ClosedByFullNameSnapshot { get; set; }

    public string? ClosedByRoleSnapshot { get; set; }

    // Mandatory once closed: the administrator's explanation, or the system text for timers.
    public string? ClosureReason { get; set; }

    public uint Version { get; set; }
    public ICollection<TransferAuditEntry> AuditEntries { get; set; }
    = new List<TransferAuditEntry>();
}
