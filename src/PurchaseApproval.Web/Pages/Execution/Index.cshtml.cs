// Lists approved requests by operational execution state without conflating closure with payment.
// "Open" shows items that still need attention (awaiting an outcome, or recorded and waiting for
// the closure timer); "History" shows closed items; both are paged with a stable order.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.Execution;

[Authorize(Policy = AppPolicies.CanExecutePayments)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(ApplicationDbContext dbContext) : PageModel
{
    public const int PageSize = 25;

    public IReadOnlyList<ExecutionQueueRow> Requests { get; private set; } = [];

    public string CurrentFilter { get; private set; } = "open";

    public PagerModel Pager { get; private set; } = new("Index", 1, 1, new Dictionary<string, string>());

    public async Task<IActionResult> OnGetAsync(string status = "open", int page = 1)
    {
        CurrentFilter = status.Trim().ToLowerInvariant();
        if (CurrentFilter is not ("open" or "history" or "all"))
        {
            return BadRequest();
        }

        var query = dbContext.PaymentRequests
            .AsNoTracking()
            .Where(item => item.Status == PaymentRequestStatus.Approved);

        query = CurrentFilter switch
        {
            "open" => query.Where(item => item.TransferOutcome == null
                || !item.TransferOutcome.IsClosed),
            "history" => query.Where(item => item.TransferOutcome != null
                && item.TransferOutcome.IsClosed),
            _ => query
        };

        page = Math.Max(1, page);
        var total = await query.CountAsync(HttpContext.RequestAborted);
        Requests = await query
            .OrderByDescending(item => item.TransferOutcome != null
                && item.TransferOutcome.RecordedAtUtc != null
                    ? item.TransferOutcome.RecordedAtUtc
                    : item.SubmittedAtUtc)
            .ThenBy(item => item.ReferenceNumber)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new ExecutionQueueRow(
                item.Id,
                item.ReferenceNumber,
                item.RequesterFullNameSnapshot,
                item.PayeeName,
                item.RequestedAccountOrCardNumber,
                item.TotalAmountRials,
                item.TransferOutcome == null
                    ? TransferStatus.AwaitingTransfer
                    : item.TransferOutcome.Status,
                item.TransferOutcome != null && item.TransferOutcome.IsClosed,
                item.TransferOutcome == null ? null : item.TransferOutcome.RecordedAtUtc,
                item.SubmittedAtUtc))
            .ToListAsync(HttpContext.RequestAborted);

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Pager = PagerModel.FromQuery("Index", page, totalPages, Request.Query);
        return Page();
    }

    public sealed record ExecutionQueueRow(
        int Id,
        string ReferenceNumber,
        string RequesterName,
        string PayeeName,
        string? RequestedDestination,
        long TotalAmountRials,
        TransferStatus TransferStatus,
        bool IsClosed,
        DateTimeOffset? RecordedAtUtc,
        DateTimeOffset SubmittedAtUtc);
}
