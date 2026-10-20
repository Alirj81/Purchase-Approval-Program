// Header record for one outbound payment request. Expense rows, attachments, the single
// decision and the single execution outcome hang off this row. Requester details are copied
// into the *Snapshot columns at submission so later profile edits never rewrite history.
namespace PurchaseApproval.Models;

public class PaymentRequest
{
    public int Id { get; set; }

    // Public business reference, for example PR-2026-3F9A0C12B7D4E6A1 (year plus random hex)
    public string ReferenceNumber { get; set; } = string.Empty;

    // Identifies which type of account submitted the request
    public RequesterType RequesterType { get; set; }
    public ApprovalDecision? Decision { get; set; }
    public uint Version { get; set; }

    public TransferOutcome? TransferOutcome { get; set; } // The outcome of the transfer associated with this payment request
    // Exactly one of these two IDs must contain a value
    public int? EmployeeRequesterId { get; set; }
    public Employee? EmployeeRequester { get; set; }

    public int? ApplicationUserRequesterId { get; set; }
    public ApplicationUser? ApplicationUserRequester { get; set; }

    // Requester information preserved as it existed during submission
    public string RequesterFullNameSnapshot { get; set; }
        = string.Empty;

    public string RequesterUsernameSnapshot { get; set; }
        = string.Empty;

    public string? RequesterPhoneNumberSnapshot { get; set; }

    public string? RequesterDepartmentSnapshot { get; set; }

    public string? RequesterBranchSnapshot { get; set; }

    public string? RequesterJobTitleSnapshot { get; set; }

    // General explanation of why the payment is required
    public string Justification { get; set; } = string.Empty;

    public string PayeeName { get; set; } = string.Empty;

    public string? RequestedAccountOrCardNumber { get; set; }

    // Authoritative sum of all saved expense rows, in whole rials
    public long TotalAmountRials { get; set; }

    public PaymentRequestStatus Status { get; set; }
        = PaymentRequestStatus.Pending;

    public DateTimeOffset SubmittedAtUtc { get; set; }
        = DateTimeOffset.UtcNow;

    // Only the hash is stored; the private tracking token is not
    public string TrackingTokenHash { get; set; } = string.Empty;

    // Idempotency key generated for one rendered submission form. Legacy rows may be null.
    public string? SubmissionKeyHash { get; set; }

    public ICollection<PaymentRequestLine> Lines { get; set; }
    = new List<PaymentRequestLine>();

    public ICollection<PaymentRequestAttachment> Attachments { get; set; }
        = new List<PaymentRequestAttachment>();
}
