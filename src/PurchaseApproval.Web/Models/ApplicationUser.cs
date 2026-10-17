// IdentityUser supplies the login, password, lockout, and role-related fields.
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
}
