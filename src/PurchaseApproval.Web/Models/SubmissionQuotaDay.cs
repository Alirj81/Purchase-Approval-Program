// One row per requester per UTC calendar day, counting successfully created requests.
// The daily quota is enforced against this row inside the same database transaction that
// inserts the request, so two concurrent submissions cannot both slip under the limit.
namespace PurchaseApproval.Models;

public class SubmissionQuotaDay
{
    public int Id { get; set; }

    // Employees and administrators have separate ID spaces, so the type is part of the key.
    public RequesterType RequesterType { get; set; }

    public int RequesterId { get; set; }

    // UTC date is the shared quota boundary; a browser timezone change cannot reset it.
    public DateOnly QuotaDateUtc { get; set; }

    public int CreatedCount { get; set; }

    // PostgreSQL xmin concurrency token; see PaymentRequest.Version for the same pattern.
    public uint Version { get; set; }
}
