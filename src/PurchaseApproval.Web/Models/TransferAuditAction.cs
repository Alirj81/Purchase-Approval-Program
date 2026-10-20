namespace PurchaseApproval.Models;

public enum TransferAuditAction
{
    AwaitingTransferCreated = 1,
    MarkedTransferred = 2,
    MarkedUnsuccessful = 3,
    ClosedByAdministrator = 4,
    TransactionDetailsCorrected = 5,
    TransferredClosedAutomatically = 6,
    UnsuccessfulClosedAutomatically = 7
}
