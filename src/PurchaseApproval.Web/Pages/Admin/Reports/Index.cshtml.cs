// Validates the administrator's visible timezone and inclusive local-date period before export.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Reports;

[Authorize(Policy = AppPolicies.CanDecideRequests)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(
    IExcelReportService excelReportService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TimeZoneId { get; set; }

    public ResolvedReportingSettings? Settings { get; private set; }

    public string? NoResultsMessage { get; private set; }

    public void OnGet()
    {
    }

    /// <summary>Validates a single local date or inclusive range and returns an XLSX without mutations.</summary>
    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!ResolvedReportingSettings.TryCreate(TimeZoneId, out var settings, out var settingsError))
        {
            ModelState.AddModelError(nameof(TimeZoneId), settingsError!);
            return Page();
        }
        Settings = settings;

        if (!ReportPeriod.TryParse(
                Date,
                From,
                To,
                ReportCalendarSystem.Gregorian,
                out var period,
                out var error))
        {
            ModelState.AddModelError(string.Empty, error!);
            return Page();
        }

        ConsolidatedExcelReportResult result;
        try
        {
            result = await excelReportService.CreateConsolidatedAsync(
                period!,
                settings!,
                HttpContext.RequestAborted);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(
                string.Empty,
                "The selected local date is not valid in the submitted reporting timezone.");
            return Page();
        }

        if (result.File is null)
        {
            NoResultsMessage =
                "No approved requests match the selected period. No workbook was generated.";
            return Page();
        }

        return File(
            result.File.Content,
            ExcelReportFile.ContentType,
            result.File.DownloadName);
    }

}
