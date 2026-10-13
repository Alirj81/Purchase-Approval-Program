namespace PurchaseApproval.Models;

public enum TransferAuditAction
{
    AwaitingTransferCreated = 1,
    MarkedTransferred = 2,
    MarkedUnsuccessful = 3,
    UnsuccessfulClosed = 4,
    TransactionDetailsCorrected = 5
}