using Microsoft.AspNetCore.Identity;

namespace PurchaseApproval.Models;

public class Administrator : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAuthenticatedAtUtc { get; set; }
}