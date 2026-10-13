namespace PurchaseApproval.Models;

public class ItemCatalogueEntry
{
    public int Id { get; set; }

    // Text displayed as a suggestion
    public string Name { get; set; } = string.Empty;

    // Used for case-insensitive duplicate detection
    public string NormalizedName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Helps display frequently used suggestions first
    public int UsageCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAtUtc { get; set; }
}