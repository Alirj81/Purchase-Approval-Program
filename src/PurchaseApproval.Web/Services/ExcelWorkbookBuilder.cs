// Creates individual and consolidated XLSX records with formula-safe user text.
// These workbooks document application state; they are not bank receipts or verified signatures.
using ClosedXML.Excel;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public interface IExcelWorkbookBuilder
{
    byte[] BuildIndividual(IndividualRequestReport report, DateTimeOffset generatedAtUtc);

    byte[] BuildConsolidated(
        IReadOnlyList<ConsolidatedRequestReportRow> rows,
        ReportPeriod period,
        ResolvedReportingSettings settings,
        DateTimeOffset generatedAtUtc);
}

public sealed class ExcelWorkbookBuilder : IExcelWorkbookBuilder
{
    private const string FontName = "Arial";
    private const string DarkBlue = "#1F4E78";
    private const string LightBlue = "#D9EAF7";
    private const string LightGray = "#E7E6E6";
    private const string BorderGray = "#B7B7B7";
    private const string MissingValue = "Not recorded";

    public byte[] BuildIndividual(
        IndividualRequestReport report,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var workbook = CreateWorkbook("Application decision and execution record");
        var sheet = workbook.Worksheets.Add("Decision record");
        PrepareSheet(sheet);

        sheet.Range("A1:H1").Merge();
        SetText(sheet.Cell("A1"), "Application decision and execution record");
        StyleTitle(sheet.Range("A1:H1"));

        sheet.Range("A2:H2").Merge();
        SetText(
            sheet.Cell("A2"),
            "This record documents the application decision and any recorded execution outcome. "
            + "It is not a bank receipt or a cryptographically verified signature.");
        sheet.Cell("A2").Style.Alignment.WrapText = true;
        sheet.Row(2).Height = 32;

        var row = 4;
        AddSection(sheet, row++, "Request and requester snapshot");
        SetPair(sheet, row, 1, "Request reference", report.ReferenceNumber);
        SetPair(sheet, row, 5, "Request status", report.Status.ToString());
        row++;
        SetPair(sheet, row, 1, "Requester type", report.RequesterType.ToString());
        SetPair(sheet, row, 5, "Submitted (UTC)", FormatUtc(report.SubmittedAtUtc));
        row++;
        SetPair(sheet, row, 1, "Requester name", report.RequesterFullName);
        SetPair(sheet, row, 5, "Username", report.RequesterUsername);
        row++;
        SetPair(sheet, row, 1, "Phone", report.RequesterPhoneNumber);
        SetPair(sheet, row, 5, "Department", report.RequesterDepartment);
        row++;
        SetPair(sheet, row, 1, "Branch", report.RequesterBranch);
        SetPair(sheet, row, 5, "Job title", report.RequesterJobTitle);
        row++;
        SetPair(sheet, row, 1, "Intended payee", report.PayeeName);
        SetPair(sheet, row, 5, "Requested account/card", report.RequestedAccountOrCardNumber);
        row++;
        SetText(sheet.Cell(row, 1), "Payment purpose");
        StyleLabel(sheet.Range(row, 1, row + 1, 1));
        sheet.Range(row, 2, row + 1, 8).Merge();
        SetText(sheet.Cell(row, 2), report.Justification);
        sheet.Cell(row, 2).Style.Alignment.WrapText = true;
        sheet.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Range(row, 2, row + 1, 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(row, 2, row + 1, 8).Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderGray);
        sheet.Row(row).Height = 27;
        sheet.Row(row + 1).Height = 27;
        row += 2;
        SetWidePair(
            sheet,
            row++,
            "Attachments",
            report.AttachmentCount == 0
                ? "No attachments recorded"
                : $"{report.AttachmentCount:N0} attachment(s) recorded; file content is not embedded");

        row++;
        AddSection(sheet, row++, "Expense rows");
        var expenseHeaderRow = row;
        var expenseHeaders = new[] { "#", "Description", "", "Invoice", "Remarks", "", "", "Amount (IRR)" };
        for (var column = 1; column <= expenseHeaders.Length; column++)
        {
            SetText(sheet.Cell(row, column), expenseHeaders[column - 1]);
        }
        sheet.Range(row, 2, row, 3).Merge();
        sheet.Range(row, 5, row, 7).Merge();
        StyleTableHeader(sheet.Range(row, 1, row, 8));
        row++;

        foreach (var line in report.Lines)
        {
            sheet.Cell(row, 1).SetValue(line.DisplayOrder + 1);
            sheet.Range(row, 2, row, 3).Merge();
            SetText(sheet.Cell(row, 2), line.Description);
            SetText(sheet.Cell(row, 4), line.HasInvoice ? "Yes" : "No");
            sheet.Range(row, 5, row, 7).Merge();
            SetText(sheet.Cell(row, 5), line.Remarks);
            SetAmount(sheet.Cell(row, 8), line.AmountRials);
            StyleBodyRow(sheet.Range(row, 1, row, 8));
            row++;
        }

        sheet.Range(row, 1, row, 7).Merge();
        SetText(sheet.Cell(row, 1), "Authoritative saved total");
        sheet.Cell(row, 1).Style.Font.Bold = true;
        SetAmount(sheet.Cell(row, 8), report.TotalAmountRials);
        sheet.Cell(row, 8).Style.Font.Bold = true;
        StyleBodyRow(sheet.Range(row, 1, row, 8));
        row++;
        SetWidePair(
            sheet,
            row++,
            "Amount in words",
            RialAmountInWords.Format(report.TotalAmountRials));
        row++;

        AddSection(sheet, row++, "Decision");
        SetPair(sheet, row, 1, "Decision result", report.Decision.DecisionType.ToString());
        SetPair(sheet, row, 5, "Decision time (UTC)", FormatUtc(report.Decision.DecidedAtUtc));
        row++;
        SetPair(sheet, row, 1, "Deciding administrator", report.Decision.DecidedByFullName);
        SetPair(sheet, row, 5, "Administrator user ID", report.Decision.DecidedByUserId.ToString());
        row++;
        // Self-approval is a recorded fact, not an error: the requester and decider are one account.
        SetWidePair(
            sheet,
            row++,
            "Self-approved",
            report.Decision.IsSelfApproval
                ? "Yes; the deciding administrator is also the request's requester"
                : "No");
        SetWidePair(
            sheet,
            row++,
            "Rejection explanation",
            report.Decision.DecisionType == ApprovalDecisionType.Rejected
                ? report.Decision.RejectionReason ?? "None was provided"
                : "Not applicable to an approved decision");

        row++;
        AddSection(sheet, row++, "Recorded execution outcome");
        row = AddTransferDetails(sheet, row, report.Transfer);

        row++;
        sheet.Range(row, 1, row, 8).Merge();
        SetText(sheet.Cell(row, 1), $"Workbook generated at {FormatUtc(generatedAtUtc)}.");
        sheet.Cell(row, 1).Style.Font.Italic = true;
        sheet.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;

        FormatIndividualColumns(sheet);
        var lastRow = row;
        sheet.Range(1, 1, lastRow, 8).Style.Font.FontName = FontName;
        sheet.Range(1, 1, lastRow, 8).Style.Font.FontSize = 10;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ConfigurePrint(sheet, 1, 1, lastRow, 8, expenseHeaderRow, pagesWide: 1);
        return Save(workbook);
    }

    public byte[] BuildConsolidated(
        IReadOnlyList<ConsolidatedRequestReportRow> rows,
        ReportPeriod period,
        ResolvedReportingSettings settings,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(settings);

        using var workbook = CreateWorkbook("Approved request export");
        var sheet = workbook.Worksheets.Add("Approved requests");
        PrepareSheet(sheet);

        var headers = new[]
        {
            "Request reference", "Requester type", "Requester name", "Username", "Phone",
            "Department", "Branch", "Job title", "Payment purpose", "Intended payee",
            "Requested account/card", "Total (IRR)", "Currency", "Submitted (report timezone)",
            "Decision", "Approver user ID", "Approved by", "Approved (report timezone)",
            "Self-approved",
            "Transfer status", "Method", "Executor user ID", "Executor", "Executor role",
            "Reported transaction time and offset", "Server recorded (report timezone)",
            "Destination account/card", "Bank", "Cheque number", "Cheque date", "Cheque payee",
            "External accounting reference", "Failure explanation", "Operational state",
            "Closure type", "Closed (report timezone)", "Closed by", "Closure reason"
        };

        sheet.Range(1, 1, 1, headers.Length).Merge();
        SetText(sheet.Cell(1, 1), "Approved request export");
        StyleTitle(sheet.Range(1, 1, 1, headers.Length));

        SetMetadata(sheet, 3, 1, "Selected period", period.Format(settings.CalendarSystem));
        SetMetadata(sheet, 3, 5, "Date basis", DateBasisLabel(settings.DateBasis));
        SetMetadata(sheet, 4, 1, "Reporting timezone", settings.CompanyTimeZone.Id);
        SetMetadata(sheet, 4, 5, "Displayed calendar", CalendarLabel(settings.CalendarSystem));
        SetMetadata(
            sheet,
            5,
            1,
            "Generated (report timezone)",
            FormatCompanyTime(generatedAtUtc, settings.CompanyTimeZone));
        SetMetadata(sheet, 5, 5, "Record count", rows.Count.ToString("N0"));
        SetMetadata(
            sheet,
            6,
            1,
            "Export total (IRR)",
            rows.Aggregate(0L, (total, item) => checked(total + item.TotalAmountRials)).ToString("N0"));

        const int headerRow = 8;
        for (var column = 1; column <= headers.Length; column++)
        {
            SetText(sheet.Cell(headerRow, column), headers[column - 1]);
        }
        StyleTableHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));
        sheet.Row(headerRow).Height = 42;

        var row = headerRow + 1;
        foreach (var item in rows)
        {
            var transfer = item.Transfer;
            SetText(sheet.Cell(row, 1), item.ReferenceNumber);
            SetText(sheet.Cell(row, 2), item.RequesterType.ToString());
            SetText(sheet.Cell(row, 3), item.RequesterFullName);
            SetText(sheet.Cell(row, 4), item.RequesterUsername);
            SetText(sheet.Cell(row, 5), item.RequesterPhoneNumber);
            SetText(sheet.Cell(row, 6), item.RequesterDepartment);
            SetText(sheet.Cell(row, 7), item.RequesterBranch);
            SetText(sheet.Cell(row, 8), item.RequesterJobTitle);
            SetText(sheet.Cell(row, 9), item.Justification);
            SetText(sheet.Cell(row, 10), item.PayeeName);
            SetIdentifier(sheet.Cell(row, 11), item.RequestedAccountOrCardNumber);
            SetAmount(sheet.Cell(row, 12), item.TotalAmountRials);
            SetText(sheet.Cell(row, 13), "IRR");
            SetCompanyTime(sheet.Cell(row, 14), item.SubmittedAtUtc, settings.CompanyTimeZone);
            SetText(sheet.Cell(row, 15), "Approved");
            SetIdentifier(sheet.Cell(row, 16), item.DecidedByUserId.ToString());
            SetText(sheet.Cell(row, 17), item.DecidedByFullName);
            SetCompanyTime(sheet.Cell(row, 18), item.DecidedAtUtc, settings.CompanyTimeZone);
            SetText(sheet.Cell(row, 19), item.IsSelfApproval ? "Yes" : "No");
            SetText(sheet.Cell(row, 20), transfer?.Status.ToString() ?? MissingValue);
            SetText(sheet.Cell(row, 21), FormatMethod(transfer));
            SetIdentifier(sheet.Cell(row, 22), transfer?.PerformedByUserId?.ToString());
            SetText(sheet.Cell(row, 23), transfer?.PerformedByFullName);
            SetText(sheet.Cell(row, 24), transfer?.PerformedByRole);
            SetText(sheet.Cell(row, 25), FormatReportedOccurrence(transfer));
            if (transfer?.RecordedAtUtc is { } recordedAt)
            {
                SetCompanyTime(sheet.Cell(row, 26), recordedAt, settings.CompanyTimeZone);
            }
            else
            {
                SetText(sheet.Cell(row, 26), null);
            }
            SetIdentifier(sheet.Cell(row, 27), transfer?.DestinationAccountOrCardNumber);
            SetText(sheet.Cell(row, 28), transfer?.BankName);
            SetIdentifier(sheet.Cell(row, 29), transfer?.ChequeNumber);
            SetText(sheet.Cell(row, 30), transfer?.ChequeDate?.ToString("yyyy-MM-dd"));
            SetText(sheet.Cell(row, 31), transfer?.ChequePayeeName);
            SetIdentifier(sheet.Cell(row, 32), transfer?.ExternalReference);
            SetText(sheet.Cell(row, 33), transfer?.FailureReason);
            SetText(sheet.Cell(row, 34), transfer is null ? MissingValue : transfer.IsClosed ? "Closed" : "Open");
            SetText(sheet.Cell(row, 35), transfer?.ClosureType?.ToString());
            if (transfer?.ClosedAtUtc is { } closedAt)
            {
                SetCompanyTime(sheet.Cell(row, 36), closedAt, settings.CompanyTimeZone);
            }
            else
            {
                SetText(sheet.Cell(row, 36), null);
            }
            SetText(
                sheet.Cell(row, 37),
                transfer?.ClosureType == TransferClosureType.Automatic
                    ? "Automatic closure service"
                    : transfer?.ClosedByFullName);
            SetText(sheet.Cell(row, 38), transfer?.ClosureReason);
            StyleBodyRow(sheet.Range(row, 1, row, headers.Length));
            row++;
        }

        var lastRow = Math.Max(headerRow, row - 1);
        sheet.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        FormatConsolidatedColumns(sheet, lastRow, headers.Length);
        sheet.Range(1, 1, lastRow, headers.Length).Style.Font.FontName = FontName;
        sheet.Range(1, 1, lastRow, headers.Length).Style.Font.FontSize = 9;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ConfigurePrint(sheet, 1, 1, lastRow, headers.Length, headerRow, pagesWide: 5);
        return Save(workbook);
    }

    private static XLWorkbook CreateWorkbook(string title)
    {
        var workbook = new XLWorkbook();
        workbook.Properties.Title = title;
        workbook.Properties.Subject = "Purchase approval application reporting";
        return workbook;
    }

    private static void PrepareSheet(IXLWorksheet sheet)
    {
        sheet.ShowGridLines = false;
        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = 10;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void AddSection(IXLWorksheet sheet, int row, string title)
    {
        sheet.Range(row, 1, row, 8).Merge();
        SetText(sheet.Cell(row, 1), title);
        var range = sheet.Range(row, 1, row, 8);
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(DarkBlue);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        sheet.Row(row).Height = 22;
    }

    private static void SetPair(
        IXLWorksheet sheet,
        int row,
        int startColumn,
        string label,
        string? value)
    {
        SetText(sheet.Cell(row, startColumn), label);
        StyleLabel(sheet.Range(row, startColumn, row, startColumn));
        sheet.Range(row, startColumn + 1, row, startColumn + 3).Merge();
        SetText(sheet.Cell(row, startColumn + 1), value);
        StyleValue(sheet.Range(row, startColumn + 1, row, startColumn + 3));
    }

    private static void SetMetadata(
        IXLWorksheet sheet,
        int row,
        int startColumn,
        string label,
        string value)
    {
        SetText(sheet.Cell(row, startColumn), label);
        StyleLabel(sheet.Range(row, startColumn, row, startColumn));
        sheet.Range(row, startColumn + 1, row, startColumn + 3).Merge();
        SetText(sheet.Cell(row, startColumn + 1), value);
        StyleValue(sheet.Range(row, startColumn + 1, row, startColumn + 3));
    }

    private static void SetWidePair(
        IXLWorksheet sheet,
        int row,
        string label,
        string? value)
    {
        SetText(sheet.Cell(row, 1), label);
        StyleLabel(sheet.Range(row, 1, row, 1));
        sheet.Range(row, 2, row, 8).Merge();
        SetText(sheet.Cell(row, 2), value);
        StyleValue(sheet.Range(row, 2, row, 8));
    }

    private static int AddTransferDetails(
        IXLWorksheet sheet,
        int row,
        TransferReportSnapshot? transfer)
    {
        if (transfer is null || transfer.Status == TransferStatus.AwaitingTransfer)
        {
            SetPair(sheet, row++, 1, "Transfer status", transfer?.Status.ToString() ?? MissingValue);
            SetWidePair(
                sheet,
                row++,
                "Execution statement",
                "Approval was recorded; no successful transfer has been recorded.");
            return row;
        }

        SetPair(sheet, row, 1, "Transfer status", transfer.Status.ToString());
        SetPair(sheet, row, 5, "Method", FormatMethod(transfer));
        row++;
        SetPair(sheet, row, 1, "Executor", transfer.PerformedByFullName);
        SetPair(sheet, row, 5, "Executor role", transfer.PerformedByRole);
        row++;
        SetPair(sheet, row, 1, "Executor user ID", transfer.PerformedByUserId?.ToString());
        SetPair(sheet, row, 5, "Recording time (UTC)",
            transfer.RecordedAtUtc is null ? null : FormatUtc(transfer.RecordedAtUtc.Value));
        row++;
        SetWidePair(sheet, row++, "Reported transaction time", FormatReportedOccurrence(transfer));

        if (transfer.Method == TransferMethod.BankTransfer)
        {
            SetPair(sheet, row, 1, "Destination account/card", transfer.DestinationAccountOrCardNumber);
            SetPair(sheet, row++, 5, "Bank", transfer.BankName);
        }
        else if (transfer.Method == TransferMethod.Cheque)
        {
            SetPair(sheet, row, 1, "Cheque number", transfer.ChequeNumber);
            SetPair(sheet, row, 5, "Cheque date", transfer.ChequeDate?.ToString("yyyy-MM-dd"));
            row++;
            SetPair(sheet, row, 1, "Cheque payee", transfer.ChequePayeeName);
            SetPair(sheet, row++, 5, "Bank", transfer.BankName);
        }

        if (transfer.Status == TransferStatus.Unsuccessful)
        {
            SetWidePair(sheet, row++, "Failure explanation", transfer.FailureReason);
            SetWidePair(
                sheet,
                row++,
                "Execution statement",
                "An unsuccessful attempt was recorded; no successful transfer has been recorded.");
        }

        SetWidePair(sheet, row++, "External accounting reference", transfer.ExternalReference);
        SetPair(sheet, row, 1, "Operational state", transfer.IsClosed ? "Closed" : "Open");
        SetPair(sheet, row, 5, "Closure type", transfer.ClosureType?.ToString());
        row++;
        if (transfer.IsClosed)
        {
            SetPair(
                sheet,
                row,
                1,
                "Closed at (UTC)",
                transfer.ClosedAtUtc is null ? null : FormatUtc(transfer.ClosedAtUtc.Value));
            SetPair(
                sheet,
                row++,
                5,
                "Closed by",
                transfer.ClosureType == TransferClosureType.Automatic
                    ? "Automatic closure service"
                    : transfer.ClosedByFullName);
            SetWidePair(sheet, row++, "Closure reason", transfer.ClosureReason);
        }

        return row;
    }

    private static void StyleTitle(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 15;
        range.Style.Font.FontColor = XLColor.FromHtml(DarkBlue);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        range.Worksheet.Row(range.FirstRow().RowNumber()).Height = 28;
    }

    private static void StyleLabel(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(LightBlue);
        range.Style.Font.Bold = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderGray);
    }

    private static void StyleValue(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.FromHtml(BorderGray);
        range.Style.Alignment.WrapText = true;
    }

    private static void StyleTableHeader(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(DarkBlue);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.White;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorderColor = XLColor.White;
    }

    private static void StyleBodyRow(IXLRange range)
    {
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml(BorderGray);
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    private static void SetText(IXLCell cell, string? value)
    {
        // SetValue writes a shared-string cell, so formula-like user input is never evaluated.
        cell.SetValue(string.IsNullOrEmpty(value) ? MissingValue : value);
        cell.Style.NumberFormat.Format = "@";
    }

    private static void SetIdentifier(IXLCell cell, string? value)
    {
        SetText(cell, value);
        cell.Style.NumberFormat.Format = "@";
    }

    private static void SetAmount(IXLCell cell, long amount)
    {
        cell.SetValue(amount);
        cell.Style.NumberFormat.Format = "#,##0";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static void SetCompanyTime(
        IXLCell cell,
        DateTimeOffset value,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        cell.SetValue(local.DateTime);
        cell.Style.NumberFormat.Format = "yyyy-mm-dd hh:mm:ss";
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    private static string FormatCompanyTime(DateTimeOffset value, TimeZoneInfo timeZone) =>
        $"{TimeZoneInfo.ConvertTime(value, timeZone):yyyy-MM-dd HH:mm:ss zzz}";

    private static string FormatReportedOccurrence(TransferReportSnapshot? transfer)
    {
        if (transfer?.TransactionOccurredAtUtc is not { } occurredAt
            || transfer.TransactionUtcOffsetMinutes is not { } offsetMinutes)
        {
            return MissingValue;
        }

        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var text = occurredAt.ToOffset(offset).ToString("yyyy-MM-dd HH:mm:ss zzz");
        // The IANA zone explains *why* that offset applied (daylight saving, local rules).
        return string.IsNullOrEmpty(transfer.TransactionTimeZoneId)
            ? text
            : $"{text} ({transfer.TransactionTimeZoneId})";
    }

    private static string FormatMethod(TransferReportSnapshot? transfer)
    {
        if (transfer?.Method is null)
        {
            return MissingValue;
        }

        return transfer.Method == TransferMethod.Other
            ? transfer.OtherMethodDescription ?? "Other"
            : transfer.Method.ToString()!;
    }

    private static string DateBasisLabel(ConsolidatedReportDateBasis basis) =>
        basis == ConsolidatedReportDateBasis.ApprovalDecision
            ? "Approval decision time"
            : "Request submission time";

    private static string CalendarLabel(ReportCalendarSystem calendarSystem) =>
        calendarSystem == ReportCalendarSystem.Gregorian
            ? "Gregorian"
            : "Persian (Solar Hijri)";

    private static void FormatIndividualColumns(IXLWorksheet sheet)
    {
        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 18;
        sheet.Column(3).Width = 14;
        sheet.Column(4).Width = 12;
        sheet.Column(5).Width = 22;
        sheet.Column(6).Width = 16;
        sheet.Column(7).Width = 14;
        sheet.Column(8).Width = 17;
    }

    private static void FormatConsolidatedColumns(
        IXLWorksheet sheet,
        int lastRow,
        int lastColumn)
    {
        var widths = new double[]
        {
            20, 15, 24, 20, 16, 18, 16, 18, 38, 24, 24, 18, 10, 22, 12, 16,
            24, 22, 14, 18, 18, 16, 24, 18, 32, 22, 24, 18, 18, 14, 24, 22, 32,
            18, 18, 22, 26, 32
        };
        for (var column = 1; column <= lastColumn; column++)
        {
            sheet.Column(column).Width = widths[column - 1];
        }

        sheet.Range(9, 1, lastRow, lastColumn).Style.Alignment.WrapText = true;
        sheet.Range(9, 12, lastRow, 12).Style.Alignment.Horizontal =
            XLAlignmentHorizontalValues.Right;
    }

    private static void ConfigurePrint(
        IXLWorksheet sheet,
        int firstRow,
        int firstColumn,
        int lastRow,
        int lastColumn,
        int repeatingHeaderRow,
        int pagesWide)
    {
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(pagesWide, 0);
        sheet.PageSetup.Margins.Top = 0.4;
        sheet.PageSetup.Margins.Bottom = 0.4;
        sheet.PageSetup.Margins.Left = 0.3;
        sheet.PageSetup.Margins.Right = 0.3;
        sheet.PageSetup.CenterHorizontally = true;
        sheet.PageSetup.PrintAreas.Add(
            sheet.Range(firstRow, firstColumn, lastRow, lastColumn).RangeAddress.ToString());
        sheet.PageSetup.SetRowsToRepeatAtTop(repeatingHeaderRow, repeatingHeaderRow);
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
