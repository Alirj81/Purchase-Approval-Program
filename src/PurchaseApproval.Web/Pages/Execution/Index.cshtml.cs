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
    public IReadOnlyList<ExecutionQueueRow> Requests { get; private set; } = [];

    public string CurrentFilter { get; private set; } = "open";

    public async Task<IActionResult> OnGetAsync(string status = "open")
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
                || item.TransferOutcome.Status == TransferStatus.AwaitingTransfer
                || (item.TransferOutcome.Status == TransferStatus.Unsuccessful
                    && !item.TransferOutcome.IsClosed)),
            "history" => query.Where(item => item.TransferOutcome != null
                && (item.TransferOutcome.Status == TransferStatus.Transferred
                    || item.TransferOutcome.IsClosed)),
            _ => query
        };

        Requests = await query
            .OrderByDescending(item => item.TransferOutcome != null
                && item.TransferOutcome.RecordedAtUtc != null
                    ? item.TransferOutcome.RecordedAtUtc
                    : item.SubmittedAtUtc)
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
