namespace PurchaseApproval.Models;

public class PaymentRequestAttachment
{
    public int Id { get; set; }

    public int PaymentRequestId { get; set; }

    public PaymentRequest PaymentRequest { get; set; }
        = null!;

    // Filename displayed to authorized users
    public string OriginalFileName { get; set; } = string.Empty;

    // For example: application/pdf or image/jpeg
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    // Allows us to detect whether file contents changed
    public string Sha256Hash { get; set; } = string.Empty;

    // PostgreSQL stores this as a bytea value
    public byte[] Content { get; set; }
        = Array.Empty<byte>();

    public DateTimeOffset UploadedAtUtc { get; set; }
        = DateTimeOffset.UtcNow;
}