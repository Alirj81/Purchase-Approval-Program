namespace PurchaseApproval.Security;

/// <summary>
/// Purpose: Names the separate cookie and claims used by employee access-key sessions.
/// Keeping these identifiers centralized prevents string mismatches between login,
/// cookie validation, authorization, and Razor Pages.
/// </summary>
public static class EmployeeAuthenticationDefaults
{
    // Structure: This shared name connects cookie registration, sign-in, sign-out, and protected pages.
    public const string AuthenticationScheme = "EmployeeAccess";
    public const string AuthenticationKindClaim = "purchase_approval:authentication_kind";
    public const string AuthenticationKind = "employee";
    // Security: Captures the credential version at sign-in so later requests can detect revoked sessions.
    public const string CredentialVersionClaim = "purchase_approval:credential_version";
}
