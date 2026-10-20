// Personal request history for the signed-in employee: own requests only, newest first, paged.
// Shows the decision and the requester-facing payment/closure labels; details open a page that
// also shows the optional rejection explanation and the originally submitted amounts.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

[Authorize(AuthenticationSchemes = EmployeeAuthenticationDefaults.AuthenticationScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(ApplicationDbContext dbContext) : PageModel
{
    public const int PageSize = 25;

    public IReadOnlyList<RequestRow> Requests { get; private set; } = [];

    public PagerModel Pager { get; private set; } = new("Index", 1, 1, new Dictionary<string, string>());

    public async Task<IActionResult> OnGetAsync(int page = 1)
    {
        var identity = User.Identities.FirstOrDefault(item => item.IsAuthenticated
            && item.AuthenticationType == EmployeeAuthenticationDefaults.AuthenticationScheme);
        if (!int.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var employeeId)
            || employeeId <= 0)
            return Forbid();

        page = Math.Max(1, page);
        var query = dbContext.PaymentRequests.AsNoTracking()
            .Where(item => item.EmployeeRequesterId == employeeId);
        var total = await query.CountAsync(HttpContext.RequestAborted);

        Requests = await query
            .OrderByDescending(item => item.SubmittedAtUtc)
            .ThenBy(item => item.ReferenceNumber)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new RequestRow(
                item.Id,
                item.ReferenceNumber,
                item.PayeeName,
                item.TotalAmountRials,
                item.Status,
                item.Status != PaymentRequestStatus.Approved
                    ? null
                    : item.TransferOutcome == null
                        ? TransferStatus.AwaitingTransfer
                        : item.TransferOutcome.Status,
                item.TransferOutcome != null && item.TransferOutcome.IsClosed,
                item.SubmittedAtUtc))
            .ToListAsync(HttpContext.RequestAborted);

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        Pager = PagerModel.FromQuery("Index", page, totalPages, Request.Query);
        return Page();
    }

    public sealed record RequestRow(
        int Id,
        string ReferenceNumber,
        string PayeeName,
        long TotalAmountRials,
        PaymentRequestStatus Status,
        TransferStatus? Payment,
        bool IsClosed,
        DateTimeOffset SubmittedAtUtc);
}
