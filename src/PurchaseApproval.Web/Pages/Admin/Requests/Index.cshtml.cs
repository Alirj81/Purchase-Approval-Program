// This file builds the administrator's payment-request queue and its status summary.
// Authorization limits the page to users allowed to approve or reject requests.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
// Entity Framework Core provides read-only projections so sensitive fields are not loaded.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.Admin.Requests;

/// <summary>Lists requests for management review, with pending requests shown by default.</summary>
[Authorize(Policy = AppPolicies.CanDecideRequests)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(ApplicationDbContext dbContext) : PageModel
{
    public IReadOnlyList<RequestQueueRow> Requests { get; private set; } = [];

    public RequestQueueCounts Counts { get; private set; } = new(0, 0, 0);

    public string CurrentFilter { get; private set; } = "pending";

    /// <summary>Loads summary counts and the selected status queue.</summary>
    public async Task<IActionResult> OnGetAsync(string status = "pending")
    {
        var normalizedStatus = status.Trim().ToLowerInvariant();
        PaymentRequestStatus? statusFilter = normalizedStatus switch
        {
            "pending" => PaymentRequestStatus.Pending,
            "approved" => PaymentRequestStatus.Approved,
            "rejected" => PaymentRequestStatus.Rejected,
            "all" => null,
            _ => (PaymentRequestStatus?)null
        };

        if (normalizedStatus is not ("pending" or "approved" or "rejected" or "all"))
        {
            return BadRequest();
        }

        CurrentFilter = normalizedStatus;

        var countsByStatus = await dbContext.PaymentRequests
            .AsNoTracking()
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, HttpContext.RequestAborted);

        Counts = new RequestQueueCounts(
            countsByStatus.GetValueOrDefault(PaymentRequestStatus.Pending),
            countsByStatus.GetValueOrDefault(PaymentRequestStatus.Approved),
            countsByStatus.GetValueOrDefault(PaymentRequestStatus.Rejected));

        var query = dbContext.PaymentRequests.AsNoTracking();
        if (statusFilter is { } selectedStatus)
        {
            query = query.Where(item => item.Status == selectedStatus);
        }

        Requests = await query
            .OrderByDescending(item => item.SubmittedAtUtc)
            .Select(item => new RequestQueueRow(
                item.Id,
                item.ReferenceNumber,
                item.RequesterFullNameSnapshot,
                item.RequesterDepartmentSnapshot,
                item.PayeeName,
                item.TotalAmountRials,
                item.Status,
                item.SubmittedAtUtc))
            .ToListAsync(HttpContext.RequestAborted);

        return Page();
    }

    /// <summary>Small projection used by the queue table.</summary>
    public sealed record RequestQueueRow(
        int Id,
        string ReferenceNumber,
        string RequesterName,
        string? Department,
        string PayeeName,
        long TotalAmountRials,
        PaymentRequestStatus Status,
        DateTimeOffset SubmittedAtUtc);

    /// <summary>Status totals displayed above the queue.</summary>
    public sealed record RequestQueueCounts(int Pending, int Approved, int Rejected)
    {
        public int All => Pending + Approved + Rejected;
    }
}
