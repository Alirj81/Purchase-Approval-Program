namespace PurchaseApproval.Security;

/// <summary>Policy names used by page authorization and startup configuration.</summary>
public static class AppPolicies
{
    /// <summary>Allows administrators to create and control employee access.</summary>
    public const string CanManageEmployees = "CanManageEmployees";

    /// <summary>Allows administrators to approve or reject payment requests.</summary>
    public const string CanDecideRequests = "CanDecideRequests";

    /// <summary>Allows administrators and accountants to record payment execution.</summary>
    public const string CanExecutePayments = "CanExecutePayments";
}
