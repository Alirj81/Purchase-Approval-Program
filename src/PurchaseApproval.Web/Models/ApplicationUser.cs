// Identity account for administrators and accountants. IdentityUser supplies the login,
// password hash, security stamp, lockout and role fields; the properties below add the
// profile snapshot source, the active flag and the temporary-credential lifecycle used by
// administrator-managed staff setup and password resets (see StaffAccountService).
using Microsoft.AspNetCore.Identity;

namespace PurchaseApproval.Models;

public class ApplicationUser : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;

    public string? Department { get; set; }

    public string? Branch { get; set; }

    public string? JobTitle { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAuthenticatedAtUtc { get; set; }

    public DateTimeOffset? DeactivatedAtUtc { get; set; }

    // True while the account holds a temporary setup/reset password. Until the holder chooses
    // a password of their own, the application permits only password change and logout.
    public bool MustChangePassword { get; set; }

    // A temporary password is single-use and short-lived; after this instant it no longer works.
    public DateTimeOffset? TemporaryCredentialExpiresAtUtc { get; set; }

    public DateTimeOffset? PasswordChangedAtUtc { get; set; }
}
