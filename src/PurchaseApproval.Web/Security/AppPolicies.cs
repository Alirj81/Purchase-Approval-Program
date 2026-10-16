namespace PurchaseApproval.Security;
///  This is the static class that defines application policies.
public static class AppPolicies
{
    /// <summary>Allows administrators to create and control employee access.</summary>
    public const string CanManageEmployees = "CanManageEmployees";

    /// <summary>Allows administrators to approve or reject payment requests.</summary>
    public const string CanDecideRequests = "CanDecideRequests";

    /// <summary>Allows administrators and accountants to record payment execution.</summary>
    public const string CanExecutePayments = "CanExecutePayments";
}
