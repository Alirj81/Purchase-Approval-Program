// Shared reporting contracts keep timestamp basis, timezone and calendar choices explicit.
using System.Globalization;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public enum ConsolidatedReportDateBasis
{
    ApprovalDecision = 1,
    Submission = 2
}

public enum ReportCalendarSystem
{
    Gregorian = 1,
    PersianSolarHijri = 2
}

public sealed record ResolvedReportingSettings(
    TimeZoneInfo CompanyTimeZone,
    ConsolidatedReportDateBasis DateBasis,
    ReportCalendarSystem CalendarSystem)
{
    public static bool TryCreate(
        string? ianaTimeZoneId,
        out ResolvedReportingSettings? settings,
        out string? error)
    {
        settings = null;
        if (string.IsNullOrWhiteSpace(ianaTimeZoneId))
        {
            error = "A reporting timezone is required. Allow device detection or enter an IANA timezone such as Europe/Berlin.";
            return false;
        }

        var normalizedId = ianaTimeZoneId.Trim();
        if (normalizedId.Length > 100
            || (!string.Equals(normalizedId, "UTC", StringComparison.Ordinal)
                && !TimeZoneInfo.TryConvertIanaIdToWindowsId(normalizedId, out _)))
        {
            error = $"'{normalizedId}' is not a valid IANA timezone ID.";
            return false;
        }

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(normalizedId);
            settings = new ResolvedReportingSettings(
                timeZone,
                ConsolidatedReportDateBasis.ApprovalDecision,
                ReportCalendarSystem.Gregorian);
            error = null;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            error = $"The reporting timezone '{normalizedId}' is not available on this server.";
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            error = $"The reporting timezone '{normalizedId}' is invalid.";
            return false;
        }
    }
}

public sealed record ReportPeriod(DateOnly From, DateOnly To)
{
    public bool IsSingleDate => From == To;

    public static bool TryParse(
        string? singleDate,
        string? from,
        string? to,
        ReportCalendarSystem calendarSystem,
        out ReportPeriod? period,
        out string? error)
    {
        period = null;
        var hasSingleDate = !string.IsNullOrWhiteSpace(singleDate);
        var hasRangeValue = !string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to);

        if (hasSingleDate && hasRangeValue)
        {
            error = "Choose either one date or a From/To range, not both.";
            return false;
        }

        if (hasSingleDate)
        {
            if (!TryParseDate(singleDate!, calendarSystem, out var date))
            {
                error = DateFormatError(calendarSystem, "date");
                return false;
            }

            period = new ReportPeriod(date, date);
            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            error = "Enter one date or both From and To dates.";
            return false;
        }

        if (!TryParseDate(from, calendarSystem, out var fromDate))
        {
            error = DateFormatError(calendarSystem, "From date");
            return false;
        }

        if (!TryParseDate(to, calendarSystem, out var toDate))
        {
            error = DateFormatError(calendarSystem, "To date");
            return false;
        }

        if (fromDate > toDate)
        {
            error = "The From date cannot be later than the To date.";
            return false;
        }

        period = new ReportPeriod(fromDate, toDate);
        error = null;
        return true;
    }

    public string Format(ReportCalendarSystem calendarSystem)
    {
        var from = FormatDate(From, calendarSystem);
        return IsSingleDate ? from : $"{from} to {FormatDate(To, calendarSystem)}";
    }

    public static string FormatDate(DateOnly date, ReportCalendarSystem calendarSystem)
    {
        if (calendarSystem == ReportCalendarSystem.Gregorian)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var calendar = new PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}");
    }

    private static bool TryParseDate(
        string value,
        ReportCalendarSystem calendarSystem,
        out DateOnly date)
    {
        if (calendarSystem == ReportCalendarSystem.Gregorian)
        {
            return DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            date = default;
            return false;
        }

        try
        {
            var calendar = new PersianCalendar();
            date = DateOnly.FromDateTime(calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static string DateFormatError(ReportCalendarSystem calendarSystem, string label) =>
        calendarSystem == ReportCalendarSystem.Gregorian
            ? $"Enter a valid {label} in yyyy-MM-dd format."
            : $"Enter a valid {label} in Solar Hijri yyyy/MM/dd format.";
}

public sealed record ReportDateWindow(DateTimeOffset StartUtc, DateTimeOffset EndExclusiveUtc)
{
    public static ReportDateWindow Create(ReportPeriod period, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(timeZone);

        var localStart = DateTime.SpecifyKind(
            period.From.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var localEndExclusive = DateTime.SpecifyKind(
            period.To.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);

        // Exclusive upper bounds include every timestamp in the selected local day.
        return new ReportDateWindow(
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEndExclusive, timeZone)));
    }
}

public sealed record ExcelReportFile(byte[] Content, string DownloadName)
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed record ConsolidatedExcelReportResult(
    ExcelReportFile? File,
    int RecordCount,
    long TotalAmountRials);

public sealed record RequestLineReportRow(
    int DisplayOrder,
    string Description,
    long AmountRials,
    bool HasInvoice,
    string? Remarks);

public sealed record DecisionReportSnapshot(
    ApprovalDecisionType DecisionType,
    int DecidedByUserId,
    string DecidedByFullName,
    string? RejectionReason,
    DateTimeOffset DecidedAtUtc,
    bool IsSelfApproval = false);

public sealed record TransferReportSnapshot(
    TransferStatus Status,
    TransferMethod? Method,
    string? OtherMethodDescription,
    string? DestinationAccountOrCardNumber,
    string? BankName,
    string? ChequeNumber,
    DateOnly? ChequeDate,
    string? ChequePayeeName,
    int? PerformedByUserId,
    string? PerformedByFullName,
    string? PerformedByRole,
    DateTimeOffset? TransactionOccurredAtUtc,
    short? TransactionUtcOffsetMinutes,
    DateTimeOffset? RecordedAtUtc,
    string? FailureReason,
    string? ExternalReference,
    bool IsClosed = false,
    TransferClosureType? ClosureType = null,
    DateTimeOffset? ClosedAtUtc = null,
    int? ClosedByUserId = null,
    string? ClosedByFullName = null,
    string? ClosedByRole = null,
    string? ClosureReason = null,
    string? TransactionTimeZoneId = null);

public sealed record IndividualRequestReport(
    int RequestId,
    string ReferenceNumber,
    RequesterType RequesterType,
    string RequesterFullName,
    string RequesterUsername,
    string? RequesterPhoneNumber,
    string? RequesterDepartment,
    string? RequesterBranch,
    string? RequesterJobTitle,
    string Justification,
    string PayeeName,
    string? RequestedAccountOrCardNumber,
    long TotalAmountRials,
    PaymentRequestStatus Status,
    DateTimeOffset SubmittedAtUtc,
    int AttachmentCount,
    DecisionReportSnapshot Decision,
    TransferReportSnapshot? Transfer,
    IReadOnlyList<RequestLineReportRow> Lines);

public sealed record ConsolidatedRequestReportRow(
    int RequestId,
    string ReferenceNumber,
    RequesterType RequesterType,
    string RequesterFullName,
    string RequesterUsername,
    string? RequesterPhoneNumber,
    string? RequesterDepartment,
    string? RequesterBranch,
    string? RequesterJobTitle,
    string Justification,
    string PayeeName,
    string? RequestedAccountOrCardNumber,
    long TotalAmountRials,
    DateTimeOffset SubmittedAtUtc,
    int DecidedByUserId,
    string DecidedByFullName,
    DateTimeOffset DecidedAtUtc,
    TransferReportSnapshot? Transfer,
    bool IsSelfApproval = false);
