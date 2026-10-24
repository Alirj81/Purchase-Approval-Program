// Creates individual and consolidated XLSX records with formula-safe user text.
// These workbooks document application state; they are not bank receipts or verified signatures.
using ClosedXML.Excel;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public interface IExcelWorkbookBuilder
{
    byte[] BuildIndividual(
        IndividualRequestReport report,
        DateTimeOffset generatedAtUtc,
        ReportLanguage language = ReportLanguage.English);

    byte[] BuildConsolidated(
        IReadOnlyList<ConsolidatedRequestReportRow> rows,
        ReportPeriod period,
        ResolvedReportingSettings settings,
        DateTimeOffset generatedAtUtc,
        ReportLanguage language = ReportLanguage.English);
}

public sealed class ExcelWorkbookBuilder : IExcelWorkbookBuilder
{
    private const string FontName = "Arial";
    private const string DarkBlue = "#1F4E78";
    private const string LightBlue = "#D9EAF7";
    private const string LightGray = "#E7E6E6";
    private const string BorderGray = "#B7B7B7";
    public byte[] BuildIndividual(
        IndividualRequestReport report,
        DateTimeOffset generatedAtUtc,
        ReportLanguage language = ReportLanguage.English)
    {
        ArgumentNullException.ThrowIfNull(report);
        var copy = new ExcelReportCopy(language);

        using var workbook = CreateWorkbook(copy["Application decision and execution record"], copy.IsPersian);
        var sheet = workbook.Worksheets.Add(copy["Decision record"]);
        PrepareSheet(sheet, copy.IsPersian);

        sheet.Range("A1:H1").Merge();
        SetText(sheet.Cell("A1"), copy["Application decision and execution record"]);
        StyleTitle(sheet.Range("A1:H1"), copy.IsPersian);

        sheet.Range("A2:H2").Merge();
        SetText(
            sheet.Cell("A2"),
            copy["This record documents the application decision and any recorded execution outcome. It is not a bank receipt or a cryptographically verified signature."]);
        sheet.Cell("A2").Style.Alignment.WrapText = true;
        sheet.Row(2).Height = 32;

        var row = 4;
        AddSection(sheet, row++, copy["Request and requester snapshot"]);
        SetPair(sheet, row, 1, copy["Request reference"], report.ReferenceNumber, leftToRight: true);
        SetPair(sheet, row, 5, copy["Request status"], copy.RequestStatus(report.Status));
        row++;
        SetPair(sheet, row, 1, copy["Requester type"], copy.RequesterType(report.RequesterType));
        SetPair(sheet, row, 5, copy["Submitted (UTC)"], FormatUtc(report.SubmittedAtUtc), leftToRight: true);
        row++;
        SetPair(sheet, row, 1, copy["Requester name"], report.RequesterFullName);
        SetPair(sheet, row, 5, copy["Username"], report.RequesterUsername, leftToRight: true);
        row++;
        SetPair(sheet, row, 1, copy["Phone"], report.RequesterPhoneNumber, leftToRight: true);
        SetPair(sheet, row, 5, copy["Department"], report.RequesterDepartment);
        row++;
        SetPair(sheet, row, 1, copy["Branch"], report.RequesterBranch);
        SetPair(sheet, row, 5, copy["Job title"], report.RequesterJobTitle);
        row++;
        SetPair(sheet, row, 1, copy["Intended payee"], report.PayeeName);
        SetPair(sheet, row, 5, copy["Requested account/card"], report.RequestedAccountOrCardNumber, leftToRight: true);
        row++;
        SetText(sheet.Cell(row, 1), copy["Payment purpose"]);
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
            copy["Attachments"],
            copy.AttachmentSummary(report.AttachmentCount));

        row++;
        AddSection(sheet, row++, copy["Expense rows"]);
        var expenseHeaderRow = row;
        var expenseHeaders = new[] { "#", copy["Description"], "", copy["Invoice"], copy["Remarks"], "", "", copy["Amount (IRR)"] };
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
            SetText(sheet.Cell(row, 4), copy[line.HasInvoice ? "Yes" : "No"]);
            sheet.Range(row, 5, row, 7).Merge();
            SetText(sheet.Cell(row, 5), line.Remarks);
            SetAmount(sheet.Cell(row, 8), line.AmountRials);
            StyleBodyRow(sheet.Range(row, 1, row, 8));
            row++;
        }

        sheet.Range(row, 1, row, 7).Merge();
        SetText(sheet.Cell(row, 1), copy["Authoritative saved total"]);
        sheet.Cell(row, 1).Style.Font.Bold = true;
        SetAmount(sheet.Cell(row, 8), report.TotalAmountRials);
        sheet.Cell(row, 8).Style.Font.Bold = true;
        StyleBodyRow(sheet.Range(row, 1, row, 8));
        row++;
        SetWidePair(
            sheet,
            row++,
            copy["Amount in words"],
            copy.AmountInWords(report.TotalAmountRials));
        row++;

        AddSection(sheet, row++, copy["Decision"]);
        SetPair(sheet, row, 1, copy["Decision result"], copy.Decision(report.Decision.DecisionType));
        SetPair(sheet, row, 5, copy["Decision time (UTC)"], FormatUtc(report.Decision.DecidedAtUtc), leftToRight: true);
        row++;
        SetPair(sheet, row, 1, copy["Deciding administrator"], report.Decision.DecidedByFullName);
        SetPair(sheet, row, 5, copy["Administrator user ID"], report.Decision.DecidedByUserId.ToString(), leftToRight: true);
        row++;
        // Self-approval is a recorded fact, not an error: the requester and decider are one account.
        SetWidePair(
            sheet,
            row++,
            copy["Self-approved"],
            copy.SelfApproval(report.Decision.IsSelfApproval));
        SetWidePair(
            sheet,
            row++,
            copy["Rejection explanation"],
            copy.RejectionExplanation(report.Decision));

        row++;
        AddSection(sheet, row++, copy["Recorded execution outcome"]);
        row = AddTransferDetails(sheet, row, report.Transfer, copy);

        row++;
        sheet.Range(row, 1, row, 8).Merge();
        SetText(sheet.Cell(row, 1), copy.GeneratedAt(FormatUtc(generatedAtUtc)));
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
        DateTimeOffset generatedAtUtc,
        ReportLanguage language = ReportLanguage.English)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(settings);
        var copy = new ExcelReportCopy(language);

        using var workbook = CreateWorkbook(copy["Approved request export"], copy.IsPersian);
        var sheet = workbook.Worksheets.Add(copy["Approved requests"]);
        PrepareSheet(sheet, copy.IsPersian);

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
        }.Select(header => copy[header]).ToArray();

        sheet.Range(1, 1, 1, headers.Length).Merge();
        SetText(sheet.Cell(1, 1), copy["Approved request export"]);
        StyleTitle(sheet.Range(1, 1, 1, headers.Length), copy.IsPersian);

        SetMetadata(sheet, 3, 1, copy["Selected period"], period.Format(settings.CalendarSystem));
        SetMetadata(sheet, 3, 5, copy["Date basis"], DateBasisLabel(settings.DateBasis, copy));
        SetMetadata(sheet, 4, 1, copy["Reporting timezone"], settings.CompanyTimeZone.Id);
        SetMetadata(sheet, 4, 5, copy["Displayed calendar"], CalendarLabel(settings.CalendarSystem, copy));
        SetMetadata(
            sheet,
            5,
            1,
            copy["Generated (report timezone)"],
            FormatCompanyTime(generatedAtUtc, settings.CompanyTimeZone));
        SetMetadata(sheet, 5, 5, copy["Record count"], rows.Count.ToString("N0"));
        SetMetadata(
            sheet,
            6,
            1,
            copy["Export total (IRR)"],
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
            SetText(sheet.Cell(row, 2), copy.RequesterType(item.RequesterType));
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
            SetText(sheet.Cell(row, 15), copy["Approved"]);
            SetIdentifier(sheet.Cell(row, 16), item.DecidedByUserId.ToString());
            SetText(sheet.Cell(row, 17), item.DecidedByFullName);
            SetCompanyTime(sheet.Cell(row, 18), item.DecidedAtUtc, settings.CompanyTimeZone);
            SetText(sheet.Cell(row, 19), copy[item.IsSelfApproval ? "Yes" : "No"]);
            SetText(sheet.Cell(row, 20), transfer is null ? copy.MissingValue : copy.Transfer(transfer.Status));
            SetText(sheet.Cell(row, 21), FormatMethod(transfer, copy));
            SetIdentifier(sheet.Cell(row, 22), transfer?.PerformedByUserId?.ToString());
            SetText(sheet.Cell(row, 23), transfer?.PerformedByFullName);
            SetText(sheet.Cell(row, 24), transfer?.PerformedByRole is null ? null : copy.Role(transfer.PerformedByRole));
            SetText(sheet.Cell(row, 25), FormatReportedOccurrence(transfer, copy));
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
            SetText(sheet.Cell(row, 34), transfer is null ? copy.MissingValue : copy[transfer.IsClosed ? "Closed" : "Open"]);
            SetText(sheet.Cell(row, 35), transfer?.ClosureType is { } closureType ? copy.Closure(closureType) : null);
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
                    ? copy["Automatic closure service"]
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

    private static XLWorkbook CreateWorkbook(string title, bool rightToLeft)
    {
        var workbook = new XLWorkbook { RightToLeft = rightToLeft };
        workbook.Properties.Title = title;
        workbook.Properties.Subject = rightToLeft
            ? "گزارش برنامهٔ تأیید خرید"
            : "Purchase approval application reporting";
        return workbook;
    }

    private static void PrepareSheet(IXLWorksheet sheet, bool rightToLeft)
    {
        sheet.RightToLeft = rightToLeft;
        sheet.ShowGridLines = false;
        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = 10;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Style.Alignment.ReadingOrder = rightToLeft
            ? XLAlignmentReadingOrderValues.RightToLeft
            : XLAlignmentReadingOrderValues.LeftToRight;
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
        string? value,
        bool leftToRight = false)
    {
        SetText(sheet.Cell(row, startColumn), label);
        StyleLabel(sheet.Range(row, startColumn, row, startColumn));
        sheet.Range(row, startColumn + 1, row, startColumn + 3).Merge();
        SetText(sheet.Cell(row, startColumn + 1), value);
        StyleValue(sheet.Range(row, startColumn + 1, row, startColumn + 3));
        if (leftToRight)
        {
            StyleLeftToRight(sheet.Range(row, startColumn + 1, row, startColumn + 3));
        }
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
        string? value,
        bool leftToRight = false)
    {
        SetText(sheet.Cell(row, 1), label);
        StyleLabel(sheet.Range(row, 1, row, 1));
        sheet.Range(row, 2, row, 8).Merge();
        SetText(sheet.Cell(row, 2), value);
        StyleValue(sheet.Range(row, 2, row, 8));
        if (leftToRight)
        {
            StyleLeftToRight(sheet.Range(row, 2, row, 8));
        }
    }

    private static int AddTransferDetails(
        IXLWorksheet sheet,
        int row,
        TransferReportSnapshot? transfer,
        ExcelReportCopy copy)
    {
        if (transfer is null || transfer.Status == TransferStatus.AwaitingTransfer)
        {
            SetPair(
                sheet,
                row++,
                1,
                copy["Transfer status"],
                transfer is null ? copy.MissingValue : copy.Transfer(transfer.Status));
            SetWidePair(
                sheet,
                row++,
                copy["Execution statement"],
                copy.AwaitingExecutionStatement);
            return row;
        }

        SetPair(sheet, row, 1, copy["Transfer status"], copy.Transfer(transfer.Status));
        SetPair(sheet, row, 5, copy["Method"], FormatMethod(transfer, copy));
        row++;
        SetPair(sheet, row, 1, copy["Executor"], transfer.PerformedByFullName);
        SetPair(sheet, row, 5, copy["Executor role"], copy.Role(transfer.PerformedByRole));
        row++;
        SetPair(sheet, row, 1, copy["Executor user ID"], transfer.PerformedByUserId?.ToString(), leftToRight: true);
        SetPair(sheet, row, 5, copy["Recording time (UTC)"],
            transfer.RecordedAtUtc is null ? null : FormatUtc(transfer.RecordedAtUtc.Value),
            leftToRight: true);
        row++;
        SetWidePair(sheet, row++, copy["Reported transaction time"], FormatReportedOccurrence(transfer, copy), leftToRight: true);

        if (transfer.Method == TransferMethod.BankTransfer)
        {
            SetPair(sheet, row, 1, copy["Destination account/card"], transfer.DestinationAccountOrCardNumber, leftToRight: true);
            SetPair(sheet, row++, 5, copy["Bank"], transfer.BankName);
        }
        else if (transfer.Method == TransferMethod.Cheque)
        {
            SetPair(sheet, row, 1, copy["Cheque number"], transfer.ChequeNumber, leftToRight: true);
            SetPair(sheet, row, 5, copy["Cheque date"], transfer.ChequeDate?.ToString("yyyy-MM-dd"), leftToRight: true);
            row++;
            SetPair(sheet, row, 1, copy["Cheque payee"], transfer.ChequePayeeName);
            SetPair(sheet, row++, 5, copy["Bank"], transfer.BankName);
        }

        if (transfer.Status == TransferStatus.Unsuccessful)
        {
            SetWidePair(sheet, row++, copy["Failure explanation"], transfer.FailureReason);
            SetWidePair(
                sheet,
                row++,
                copy["Execution statement"],
                copy.UnsuccessfulExecutionStatement);
        }

        SetWidePair(sheet, row++, copy["External accounting reference"], transfer.ExternalReference, leftToRight: true);
        SetPair(sheet, row, 1, copy["Operational state"], copy[transfer.IsClosed ? "Closed" : "Open"]);
        SetPair(sheet, row, 5, copy["Closure type"], transfer.ClosureType is { } closureType ? copy.Closure(closureType) : null);
        row++;
        if (transfer.IsClosed)
        {
            SetPair(
                sheet,
                row,
                1,
                copy["Closed at (UTC)"],
                transfer.ClosedAtUtc is null ? null : FormatUtc(transfer.ClosedAtUtc.Value),
                leftToRight: true);
            SetPair(
                sheet,
                row++,
                5,
                copy["Closed by"],
                transfer.ClosureType == TransferClosureType.Automatic
                    ? copy["Automatic closure service"]
                    : transfer.ClosedByFullName);
            SetWidePair(sheet, row++, copy["Closure reason"], transfer.ClosureReason);
        }

        return row;
    }

    private static void StyleTitle(IXLRange range, bool rightToLeft)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 15;
        range.Style.Font.FontColor = XLColor.FromHtml(DarkBlue);
        range.Style.Alignment.Horizontal = rightToLeft
            ? XLAlignmentHorizontalValues.Right
            : XLAlignmentHorizontalValues.Left;
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

    private static void StyleLeftToRight(IXLRange range)
    {
        range.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.LeftToRight;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
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
        cell.SetValue(string.IsNullOrEmpty(value) ? "—" : value);
        cell.Style.NumberFormat.Format = "@";
    }

    private static void SetIdentifier(IXLCell cell, string? value)
    {
        SetText(cell, value);
        cell.Style.NumberFormat.Format = "@";
        cell.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.LeftToRight;
    }

    private static void SetAmount(IXLCell cell, long amount)
    {
        cell.SetValue(amount);
        cell.Style.NumberFormat.Format = "#,##0";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        cell.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.LeftToRight;
    }

    private static void SetCompanyTime(
        IXLCell cell,
        DateTimeOffset value,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        cell.SetValue(local.DateTime);
        cell.Style.NumberFormat.Format = "yyyy-mm-dd hh:mm:ss";
        cell.Style.Alignment.ReadingOrder = XLAlignmentReadingOrderValues.LeftToRight;
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    private static string FormatCompanyTime(DateTimeOffset value, TimeZoneInfo timeZone) =>
        $"{TimeZoneInfo.ConvertTime(value, timeZone):yyyy-MM-dd HH:mm:ss zzz}";

    private static string FormatReportedOccurrence(
        TransferReportSnapshot? transfer,
        ExcelReportCopy copy)
    {
        if (transfer?.TransactionOccurredAtUtc is not { } occurredAt
            || transfer.TransactionUtcOffsetMinutes is not { } offsetMinutes)
        {
            return copy.MissingValue;
        }

        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var text = occurredAt.ToOffset(offset).ToString("yyyy-MM-dd HH:mm:ss zzz");
        // The IANA zone explains *why* that offset applied (daylight saving, local rules).
        return string.IsNullOrEmpty(transfer.TransactionTimeZoneId)
            ? text
            : $"{text} ({transfer.TransactionTimeZoneId})";
    }

    private static string FormatMethod(
        TransferReportSnapshot? transfer,
        ExcelReportCopy copy)
    {
        if (transfer?.Method is null)
        {
            return copy.MissingValue;
        }

        return transfer.Method == TransferMethod.Other
            ? transfer.OtherMethodDescription ?? copy["Other"]
            : copy.Method(transfer.Method.Value);
    }

    private static string DateBasisLabel(
        ConsolidatedReportDateBasis basis,
        ExcelReportCopy copy) => copy[
            basis == ConsolidatedReportDateBasis.ApprovalDecision
                ? "Approval decision time"
                : "Request submission time"];

    private static string CalendarLabel(
        ReportCalendarSystem calendarSystem,
        ExcelReportCopy copy) => copy[
            calendarSystem == ReportCalendarSystem.Gregorian
                ? "Gregorian"
                : "Persian (Solar Hijri)"];

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
