namespace PurchaseApproval.Models;

// Management decision only. Payment execution is tracked separately by TransferStatus, and
// operational closure by TransferOutcome.IsClosed, so this enum deliberately has no
// "Transferred" value (see the decision/execution/closure separation in the README).
public enum PaymentRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}
