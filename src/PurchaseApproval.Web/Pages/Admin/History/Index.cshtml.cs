// Hosts the administrator-wide, read-only history with explicit timestamp and timezone filtering.
// Submission, decision, execution, and closure timestamps remain separate in both filters and rows.
// Without an explicit date filter the page shows the last N calendar months by submission date
// (a view limit, never deletion) in the visible timezone, and says so above the table.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.History;

[Authorize(Policy = AppPolicies.CanDecideRequests)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(
    IAdministratorHistoryService historyService,
    IOptions<ApplicationLimitsOptions> limits,
    TimeProvider timeProvider) : PageModel
{
    [BindProperty(SupportsGet = true)]
    [StringLength(200)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public PaymentRequestStatus? ApprovalStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public TransferStatus? ExecutionOutcome { get; set; }

    [BindProperty(SupportsGet = true)]
    public HistoryClosureFilter Closure { get; set; } = HistoryClosureFilter.All;

    [BindProperty(SupportsGet = true)]
    public HistoryTimestampField TimestampField { get; set; } = HistoryTimestampField.Submission;

    [BindProperty(SupportsGet = true)]
    public HistorySortDirection Sort { get; set; } = HistorySortDirection.NewestFirst;

    [BindProperty(SupportsGet = true)]
    public RequesterType? RequesterType { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? RequesterId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool SelfApprovedOnly { get; set; }

    /// <summary>True to lift the default recent-months window and search every retained record.</summary>
    [BindProperty(SupportsGet = true)]
    public bool AllDates { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    [StringLength(200)]
    public string? TimeZoneId { get; set; }

    // Bound as "pageNumber" (see PagerModel.QueryParameter): "page" is the Razor Pages route value
    // holding the page path, so binding this property to "page" fails validation on every request.
    [BindProperty(SupportsGet = true, Name = PagerModel.QueryParameter)]
    public int PageNumber { get; set; } = 1;

    public AdministratorHistoryPage Result { get; private set; } = new([], 0, 1, 50);

    public PagerModel Pager { get; private set; } = new("Index", 1, 1, new Dictionary<string, string>());

    /// <summary>English key describing the active date basis; localized in the view.</summary>
    public string DateFilterLabelKey { get; private set; } = "No date filter is applied.";

    public object[] DateFilterLabelArgs { get; private set; } = [];

    public int DefaultHistoryMonths => limits.Value.DefaultHistoryMonths;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Enum.IsDefined(Closure)
            || !Enum.IsDefined(TimestampField)
            || !Enum.IsDefined(Sort)
            || (ApprovalStatus is not null && !Enum.IsDefined(ApprovalStatus.Value))
            || (ExecutionOutcome is not null && !Enum.IsDefined(ExecutionOutcome.Value))
            || (RequesterType is not null && !Enum.IsDefined(RequesterType.Value)))
        {
            return BadRequest();
        }

        // A requester ID is only meaningful together with its type: employee and administrator
        // IDs are separate sequences, so an ID alone would silently show the wrong person.
        if (RequesterId is not null && RequesterType is null)
        {
            ModelState.AddModelError(nameof(RequesterType), "Choose the requester type when filtering by requester ID.");
        }

        ReportPeriod? period = null;
        TimeZoneInfo? timeZone = null;
        ReportDateWindow? submissionWindow = null;
        var hasDateFilter = !string.IsNullOrWhiteSpace(Date)
            || !string.IsNullOrWhiteSpace(From)
            || !string.IsNullOrWhiteSpace(To);
        if (hasDateFilter)
        {
            if (!ReportPeriod.TryParse(
                    Date,
                    From,
                    To,
                    ReportCalendarSystem.Gregorian,
                    out period,
                    out var periodError))
            {
                ModelState.AddModelError(string.Empty, periodError!);
            }
            else if (!TryResolveTimeZone(TimeZoneId, out timeZone, out var timeZoneError))
            {
                ModelState.AddModelError(nameof(TimeZoneId), timeZoneError!);
            }
            else
            {
                var window = ReportDateWindow.Create(period!, timeZone!);
                DateFilterLabelKey = "Filtering {0} for {1} in {2}. UTC boundary: {3} to before {4}.";
                DateFilterLabelArgs =
                [
                    TimestampLabel(TimestampField),
                    period!.Format(ReportCalendarSystem.Gregorian),
                    timeZone!.Id,
                    window.StartUtc.ToString("u"),
                    window.EndExclusiveUtc.ToString("u")
                ];
            }
        }
        else if (!AllDates)
        {
            // Default recent window: last N calendar months by submission date, in the visible
            // timezone (device zone when supplied, otherwise UTC) so the boundary is reproducible.
            var zone = TryResolveTimeZone(TimeZoneId, out var resolved, out _) ? resolved! : TimeZoneInfo.Utc;
            var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
            var defaultPeriod = new ReportPeriod(todayLocal.AddMonths(-DefaultHistoryMonths), todayLocal);
            submissionWindow = ReportDateWindow.Create(defaultPeriod, zone);
            DateFilterLabelKey = "Showing the last {0} calendar months by submission date ({1} in {2}). Use a date range or 'All dates' for older records.";
            DateFilterLabelArgs = [DefaultHistoryMonths, defaultPeriod.Format(ReportCalendarSystem.Gregorian), zone.Id];
        }
        else
        {
            DateFilterLabelKey = "Showing all retained records without a date filter.";
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        Result = await historyService.SearchAsync(
            new AdministratorHistoryFilter(
                Search,
                ApprovalStatus,
                ExecutionOutcome,
                Closure,
                TimestampField,
                Sort,
                period,
                timeZone,
                RequesterType,
                RequesterId,
                SelfApprovedOnly ? true : null,
                Math.Max(1, PageNumber),
                limits.Value.HistoryPageSize,
                submissionWindow),
            HttpContext.RequestAborted);
        Pager = PagerModel.FromQuery("Index", Result.Page, Result.TotalPages, Request.Query);
        return Page();
    }

    public static string TimestampLabel(HistoryTimestampField field) => field switch
    {
        HistoryTimestampField.Submission => "submission timestamp",
        HistoryTimestampField.Decision => "decision timestamp",
        HistoryTimestampField.ExecutionRecording => "immutable server execution-recording timestamp",
        HistoryTimestampField.Closure => "closure timestamp",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static bool TryResolveTimeZone(
        string? id,
        out TimeZoneInfo? timeZone,
        out string? error)
    {
        timeZone = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "Enter the device or business timezone used for the date boundary.";
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            error = null;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            error = $"The timezone '{id}' is not available on this server.";
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            error = $"The timezone '{id}' is invalid.";
            return false;
        }
    }
}
