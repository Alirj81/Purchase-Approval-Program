namespace PurchaseApproval.Models;

/// <summary>Records whether an execution item was closed by policy or by an administrator.</summary>
public enum TransferClosureType
{
    Automatic = 1,
    Administrator = 2
}
