namespace PurchaseApproval.Security;

/// <summary>Role names shared by account setup and authorization rules.</summary>
public static class AppRoles
{
    /// <summary>Management users who can administer access and decide requests.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Finance users who can record payment execution.</summary>
    public const string Accountant = "Accountant";

    /// <summary>All privileged roles that must exist in ASP.NET Core Identity.</summary>
    public static readonly string[] All = [Administrator, Accountant];
}
