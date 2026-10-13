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

    // The time the user reports that the transaction happened
    public DateTimeOffset? TransactionOccurredAtUtc { get; set; }

    // Preserves the timezone offset entered by the device
    public short? TransactionUtcOffsetMinutes { get; set; }

    // The time our server received and saved the result
    public DateTimeOffset? RecordedAtUtc { get; set; }

    public string? FailureReason { get; set; }

    // Bank reference, Sepidar reference, or similar identifier
    public string? ExternalReference { get; set; }

    // Transferred is closed automatically.
    // Unsuccessful can be closed manually.
    public bool IsClosed { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    public int? ClosedByUserId { get; set; }

    public ApplicationUser? ClosedByUser { get; set; }

    public string? ClosedByFullNameSnapshot { get; set; }

    public string? ClosureReason { get; set; }

    public uint Version { get; set; }
    public ICollection<TransferAuditEntry> AuditEntries { get; set; }
    = new List<TransferAuditEntry>();
}