namespace PurchaseApproval.Models;

public class PaymentRequestLine
{
    public int Id { get; set; }

    public int PaymentRequestId { get; set; }

    public PaymentRequest PaymentRequest { get; set; }
        = null!;

 public string Description { get; set; } = string.Empty;

public long AmountRials { get; set; }

public bool HasInvoice { get; set; }

public string? Remarks { get; set; }

public int DisplayOrder { get; set; }
}