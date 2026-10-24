namespace PurchaseApproval.Models;

public class Employee
{
    public int Id { get; set; }

    // Profile
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Department { get; set; }
    public string? Branch { get; set; }
    public string? JobTitle { get; set; }
    public string? AdminNotes { get; set; }

    // Authentication
    public string AccessKeyHash { get; set; } = string.Empty;
    public int CredentialVersion { get; set; } = 1;
    public EmployeeAccessStatus AccessStatus { get; set; }
        = EmployeeAccessStatus.Active;

    // Audit information
    public DateTimeOffset CreatedAtUtc { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAuthenticatedAtUtc { get; set; }
    public DateTimeOffset? DisabledAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
}

public enum EmployeeAccessStatus
{
    Active = 1,
    Disabled = 2,
    Revoked = 3,
    Deleted = 4
}
