// View model handed to the shared _RequestForm partial by both request-creation pages.
namespace PurchaseApproval.Web.Pages;

public sealed record RequestFormLine(string Description, long AmountRials, bool HasInvoice, string? Remarks);

public sealed record RequestFormViewModel(
    string PayeeName,
    string Justification,
    string? RequestedAccountOrCardNumber,
    IReadOnlyList<RequestFormLine> Lines,
    int MaximumAttachmentCount,
    long MaximumAttachmentBytes)
{
    public long MaximumAttachmentMiB => MaximumAttachmentBytes / (1024 * 1024);
}
