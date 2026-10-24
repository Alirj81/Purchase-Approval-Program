// Exercises report rules with EF Core InMemory and inspects the resulting ClosedXML workbooks.
using System.Reflection;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;
using AdminReportPage = PurchaseApproval.Web.Pages.Admin.Reports.IndexModel;
using AdminRequestPage = PurchaseApproval.Web.Pages.Admin.Requests.DetailsModel;

namespace PurchaseApproval.Web.Tests;

public sealed class ExcelReportingTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(typeof(AdminReportPage))]
    [InlineData(typeof(AdminRequestPage))]
    public void ExportPages_RequireTheExistingAdministratorDecisionPolicy(Type pageType)
    {
        var authorization = pageType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal(AppPolicies.CanDecideRequests, authorization.Policy);
    }

    [Fact]
    public async Task IndividualApprovedEmployee_UsesSnapshotsAndDoesNotEmbedAttachments()
    {
        await using var dbContext = CreateDbContext();
        var request = CreateApprovedRequest(
            1,
            "PR-2026-EMPLOYEE",
            RequesterType.Employee,
            "Historical Employee",
            "00123",
            GeneratedAt.AddHours(-4));
        request.Justification = "=HYPERLINK(\"https://invalid.example\",\"click\")";
        request.RequestedAccountOrCardNumber = "0012345000";
        request.Lines.Add(new PaymentRequestLine
        {
            Id = 11,
            DisplayOrder = 0,
            Description = "=2+2",
            AmountRials = 400_000,
            HasInvoice = true,
            Remarks = "+formula-like text"
        });
        request.Lines.Add(new PaymentRequestLine
        {
            Id = 12,
            DisplayOrder = 1,
            Description = "Second expense",
            AmountRials = 600_000,
            HasInvoice = false
        });
        request.Attachments.Add(new PaymentRequestAttachment
        {
            Id = 21,
            OriginalFileName = "secret-evidence.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 19,
            Sha256Hash = new string('A', 64),
            Content = Encoding.UTF8.GetBytes("DO-NOT-EMBED-SECRET")
        });
        request.Attachments.Add(new PaymentRequestAttachment
        {
            Id = 22,
            OriginalFileName = "second.png",
            ContentType = "image/png",
            FileSizeBytes = 8,
            Sha256Hash = new string('B', 64),
            Content = [1, 2, 3, 4, 5, 6, 7, 8]
        });
        dbContext.PaymentRequests.Add(request);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext);
        var file = await service.CreateIndividualAsync(request.Id);

        Assert.NotNull(file);
        using var workbook = Open(file!.Content);
        var sheet = workbook.Worksheet("Decision record");
        var text = CellText(sheet);

        Assert.Contains("Employee", text);
        Assert.Contains("Historical Employee", text);
        Assert.Contains("00123", text);
        Assert.Contains("0012345000", text);
        Assert.Contains("=HYPERLINK(\"https://invalid.example\",\"click\")", text);
        Assert.Contains("=2+2", text);
        Assert.Contains("2 attachment(s) recorded; file content is not embedded", text);
        Assert.DoesNotContain("secret-evidence.pdf", text);
        Assert.DoesNotContain("DO-NOT-EMBED-SECRET", text);
        Assert.Contains("One million Iranian rials only", text);
        Assert.Contains("Approval was recorded; no successful transfer has been recorded.", text);
        Assert.Contains(sheet.CellsUsed(), cell =>
            cell.DataType == XLDataType.Number && cell.GetValue<long>() == 1_000_000);
        Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.HasFormula);
        Assert.Equal(XLPageOrientation.Portrait, sheet.PageSetup.PageOrientation);
        Assert.NotEmpty(sheet.PageSetup.PrintAreas);

        var unchanged = await dbContext.PaymentRequests
            .AsNoTracking()
            .Include(item => item.TransferOutcome)
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(PaymentRequestStatus.Approved, unchanged.Status);
        Assert.Equal(TransferStatus.AwaitingTransfer, unchanged.TransferOutcome!.Status);
        Assert.False(dbContext.ChangeTracker.HasChanges());
    }

    [Fact]
    public void IndividualRejectedAdministrator_ShowsMissingOptionalExplanationClearly()
    {
        var report = CreateIndividualReport(
            RequesterType.Administrator,
            ApprovalDecisionType.Rejected,
            rejectionReason: null,
            transfer: null);
        var builder = new ExcelWorkbookBuilder();

        using var workbook = Open(builder.BuildIndividual(report, GeneratedAt));
        var text = CellText(workbook.Worksheet("Decision record"));

        Assert.Contains("Administrator", text);
        Assert.Contains("Rejected", text);
        Assert.Contains("None was provided", text);
        Assert.Contains("No attachments recorded", text);
    }

    [Fact]
    public void IndividualTransferredOutput_SeparatesApprovalFromSuccessfulExecution()
    {
        var transfer = new TransferReportSnapshot(
            TransferStatus.Transferred,
            TransferMethod.BankTransfer,
            null,
            "0000123400",
            "Test Bank",
            null,
            null,
            null,
            18,
            "Execution User",
            AppRoles.Accountant,
            new DateTimeOffset(2026, 10, 19, 5, 0, 0, TimeSpan.Zero),
            210,
            new DateTimeOffset(2026, 10, 19, 5, 2, 0, TimeSpan.Zero),
            null,
            "0000456");
        var report = CreateIndividualReport(
            RequesterType.Employee,
            ApprovalDecisionType.Approved,
            rejectionReason: null,
            transfer);

        using var workbook = Open(new ExcelWorkbookBuilder().BuildIndividual(report, GeneratedAt));
        var text = CellText(workbook.Worksheet("Decision record"));

        Assert.Contains("Transferred", text);
        Assert.Contains("Execution User", text);
        Assert.Contains(AppRoles.Accountant, text);
        Assert.Contains("2026-10-19 08:30:00 +03:30", text);
        Assert.Contains("2026-10-19 05:02:00 UTC", text);
        Assert.Contains("0000123400", text);
        Assert.Contains("0000456", text);
        Assert.Contains("Application decision and execution record", text);
        Assert.Contains(text, value => value.Contains("not a bank receipt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersianExports_AreTranslatedRightToLeftAndKeepTypedFinancialValues()
    {
        await using var dbContext = CreateDbContext();
        var request = CreateApprovedRequest(
            1,
            "PR-2026-FA",
            RequesterType.Employee,
            "علی رحیمی",
            "ali.rahimi",
            GeneratedAt.AddHours(-4));
        request.Lines.Add(new PaymentRequestLine
        {
            Id = 11,
            DisplayOrder = 0,
            Description = "خرید کاغذ",
            AmountRials = 1_000_000,
            HasInvoice = true
        });
        dbContext.Add(request);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = CreateService(dbContext);
        var individualFile = await service.CreateIndividualAsync(
            request.Id,
            ReportLanguage.Persian);

        Assert.NotNull(individualFile);
        Assert.Contains("-fa.xlsx", individualFile!.DownloadName, StringComparison.Ordinal);
        using (var individualWorkbook = Open(individualFile.Content))
        {
            var sheet = individualWorkbook.Worksheet("گزارش درخواست");
            var text = CellText(sheet);
            Assert.True(sheet.RightToLeft);
            Assert.Contains("گزارش تصمیم و پرداخت درخواست", text);
            Assert.Contains("تأیید شده", text);
            Assert.Contains("یک میلیون ریال", text);
            Assert.Equal(XLAlignmentReadingOrderValues.LeftToRight,
                sheet.Cell("B5").Style.Alignment.ReadingOrder);
            Assert.Contains(sheet.CellsUsed(), cell =>
                cell.DataType == XLDataType.Number && cell.GetValue<long>() == 1_000_000);
            Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.HasFormula);
        }

        var period = new ReportPeriod(
            DateOnly.FromDateTime(GeneratedAt.UtcDateTime),
            DateOnly.FromDateTime(GeneratedAt.UtcDateTime));
        var settings = new ResolvedReportingSettings(
            TimeZoneInfo.Utc,
            ConsolidatedReportDateBasis.ApprovalDecision,
            ReportCalendarSystem.Gregorian);
        var consolidated = await service.CreateConsolidatedAsync(
            period,
            settings,
            ReportLanguage.Persian);

        Assert.NotNull(consolidated.File);
        Assert.Contains("approved-requests-fa-", consolidated.File!.DownloadName, StringComparison.Ordinal);
        using var consolidatedWorkbook = Open(consolidated.File.Content);
        var consolidatedSheet = consolidatedWorkbook.Worksheet("درخواست‌های تأییدشده");
        Assert.True(consolidatedSheet.RightToLeft);
        Assert.Equal("شمارهٔ پیگیری", consolidatedSheet.Cell("A8").GetString());
        Assert.Equal("تأیید شده", consolidatedSheet.Cell("O9").GetString());
        Assert.Equal(XLDataType.Number, consolidatedSheet.Cell("L9").DataType);
        Assert.Equal(XLDataType.DateTime, consolidatedSheet.Cell("N9").DataType);
        Assert.DoesNotContain(consolidatedSheet.CellsUsed(), cell => cell.HasFormula);
    }

    [Fact]
    public async Task ConsolidatedSingleDate_FiltersBoundariesAndPreventsChildRowDuplicates()
    {
        await using var dbContext = CreateDbContext();
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Company +03:30",
            TimeSpan.FromMinutes(210),
            "Company +03:30",
            "Company +03:30");
        var settings = new ResolvedReportingSettings(
            timeZone,
            ConsolidatedReportDateBasis.ApprovalDecision,
            ReportCalendarSystem.Gregorian);
        var period = new ReportPeriod(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19));
        var window = ReportDateWindow.Create(period, timeZone);

        var employee = CreateApprovedRequest(
            1,
            "PR-EMPLOYEE",
            RequesterType.Employee,
            "Employee Snapshot",
            "emp-001",
            window.StartUtc);
        employee.Justification = "=SUM(1,2)";
        employee.RequestedAccountOrCardNumber = "000001111";
        AddDuplicateRiskChildren(employee, 100);

        var administrator = CreateApprovedRequest(
            2,
            "PR-ADMIN",
            RequesterType.Administrator,
            "Admin Snapshot",
            "admin-001",
            window.EndExclusiveUtc.AddTicks(-1));
        AddDuplicateRiskChildren(administrator, 200);

        var outside = CreateApprovedRequest(
            3,
            "PR-OUTSIDE",
            RequesterType.Employee,
            "Outside",
            "outside",
            window.EndExclusiveUtc);
        var pending = CreateBaseRequest(4, "PR-PENDING", RequesterType.Employee);
        var rejected = CreateBaseRequest(5, "PR-REJECTED", RequesterType.Employee);
        rejected.Status = PaymentRequestStatus.Rejected;
        rejected.Decision = CreateDecision(rejected, ApprovalDecisionType.Rejected, window.StartUtc);

        dbContext.AddRange(employee, administrator, outside, pending, rejected);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var result = await CreateService(dbContext).CreateConsolidatedAsync(period, settings);

        Assert.NotNull(result.File);
        Assert.Equal(2, result.RecordCount);
        Assert.Equal(2_000_000, result.TotalAmountRials);
        using var workbook = Open(result.File!.Content);
        var sheet = workbook.Worksheet("Approved requests");
        Assert.Equal(["PR-EMPLOYEE", "PR-ADMIN"],
            sheet.Range("A9:A10").Cells().Select(cell => cell.GetString()).ToArray());
        Assert.Equal("000001111", sheet.Cell("K9").GetString());
        Assert.Equal("=SUM(1,2)", sheet.Cell("I9").GetString());
        Assert.False(sheet.Cell("I9").HasFormula);
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber() - 8);
        Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        Assert.NotEmpty(sheet.PageSetup.PrintAreas);
        Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.HasFormula);
        Assert.False(dbContext.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ConsolidatedDateBasis_UsesConfiguredFieldAndEmptyResultsAreClear()
    {
        await using var dbContext = CreateDbContext();
        var selectedDate = new DateOnly(2026, 10, 19);
        var period = new ReportPeriod(selectedDate, selectedDate);
        var inside = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var outside = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var request = CreateApprovedRequest(
            1,
            "PR-BASIS",
            RequesterType.Employee,
            "Employee",
            "employee",
            outside);
        request.SubmittedAtUtc = inside;
        dbContext.Add(request);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var submissionSettings = new ResolvedReportingSettings(
            TimeZoneInfo.Utc,
            ConsolidatedReportDateBasis.Submission,
            ReportCalendarSystem.Gregorian);
        var approvalSettings = submissionSettings with
        {
            DateBasis = ConsolidatedReportDateBasis.ApprovalDecision
        };
        var service = CreateService(dbContext);

        var bySubmission = await service.CreateConsolidatedAsync(period, submissionSettings);
        var byApproval = await service.CreateConsolidatedAsync(period, approvalSettings);

        Assert.Equal(1, bySubmission.RecordCount);
        Assert.NotNull(bySubmission.File);
        Assert.Equal(0, byApproval.RecordCount);
        Assert.Null(byApproval.File);
    }

    [Fact]
    public void PeriodValidation_RejectsReversedRangesAndParsesBothSupportedCalendars()
    {
        Assert.False(ReportPeriod.TryParse(
            null,
            "2026-10-20",
            "2026-10-19",
            ReportCalendarSystem.Gregorian,
            out _,
            out var reversedError));
        Assert.Contains("cannot be later", reversedError, StringComparison.Ordinal);

        Assert.True(ReportPeriod.TryParse(
            "1405/07/27",
            null,
            null,
            ReportCalendarSystem.PersianSolarHijri,
            out var persian,
            out _));
        Assert.Equal(new DateOnly(2026, 10, 19), persian!.From);
    }

    [Fact]
    public void ConfirmedSettings_RequireAValidIanaTimezoneAndFixBasisAndCalendar()
    {
        Assert.False(ResolvedReportingSettings.TryCreate(null, out _, out var missingError));
        Assert.Contains("required", missingError, StringComparison.OrdinalIgnoreCase);
        Assert.False(ResolvedReportingSettings.TryCreate(
            "Definitely/Not_A_Timezone",
            out _,
            out var invalidError));
        Assert.Contains("IANA", invalidError, StringComparison.Ordinal);

        Assert.True(ResolvedReportingSettings.TryCreate("Europe/Berlin", out var settings, out _));
        Assert.Equal(ConsolidatedReportDateBasis.ApprovalDecision, settings!.DateBasis);
        Assert.Equal(ReportCalendarSystem.Gregorian, settings.CalendarSystem);
        Assert.Equal("Europe/Berlin", settings.CompanyTimeZone.Id);
    }

    [Theory]
    [InlineData("2026-10-19", "2026-10-19", true)]
    [InlineData("2026-01-31", "2026-02-02", false)]
    [InlineData("2026-12-31", "2027-01-01", false)]
    public void GregorianRange_ParsesEqualMonthAndYearBoundaries(
        string from,
        string to,
        bool expectedSingleDate)
    {
        Assert.True(ReportPeriod.TryParse(
            null,
            from,
            to,
            ReportCalendarSystem.Gregorian,
            out var period,
            out _));
        Assert.Equal(expectedSingleDate, period!.IsSingleDate);
    }

    [Theory]
    [InlineData("2026-03-29", "2026-03-29", 23)]
    [InlineData("2026-10-25", "2026-10-25", 25)]
    [InlineData("2026-03-28", "2026-03-30", 71)]
    public void DateWindow_UsesExclusiveNextMidnightAcrossDaylightSavingChanges(
        string from,
        string to,
        int expectedHours)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var period = new ReportPeriod(DateOnly.Parse(from), DateOnly.Parse(to));

        var window = ReportDateWindow.Create(period, timeZone);

        Assert.Equal(TimeSpan.FromHours(expectedHours), window.EndExclusiveUtc - window.StartUtc);
    }

    [Fact]
    public async Task ConsolidatedMultiDayRange_IncludesBothLocalBoundaryDatesAndWorkbookMetadata()
    {
        await using var dbContext = CreateDbContext();
        Assert.True(ResolvedReportingSettings.TryCreate("Europe/Berlin", out var settings, out _));
        var period = new ReportPeriod(new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1));
        var window = ReportDateWindow.Create(period, settings!.CompanyTimeZone);

        dbContext.AddRange(
            CreateApprovedRequest(1, "PR-FIRST", RequesterType.Employee, "Employee", "employee", window.StartUtc),
            CreateApprovedRequest(2, "PR-LAST", RequesterType.Administrator, "Admin", "admin", window.EndExclusiveUtc.AddTicks(-1)),
            CreateApprovedRequest(3, "PR-OUTSIDE", RequesterType.Employee, "Outside", "outside", window.EndExclusiveUtc));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var result = await CreateService(dbContext).CreateConsolidatedAsync(period, settings);

        Assert.Equal(2, result.RecordCount);
        Assert.Equal(2_000_000, result.TotalAmountRials);
        using var workbook = Open(result.File!.Content);
        Assert.Single(workbook.Worksheets);
        var text = CellText(workbook.Worksheet("Approved requests"));
        Assert.Contains("2026-12-31 to 2027-01-01", text);
        Assert.Contains("Approval decision time", text);
        Assert.Contains("Europe/Berlin", text);
        Assert.Contains("Gregorian", text);
    }

    [Theory]
    [InlineData(1, "One Iranian rial only")]
    [InlineData(12_500_000, "Twelve million five hundred thousand Iranian rials only")]
    [InlineData(10_000_000_000, "Ten billion Iranian rials only")]
    public void IndividualAmountInWords_MatchesTheDocumentedPaymentForm(
        long amountRials,
        string expected)
    {
        var report = CreateIndividualReport(
            RequesterType.Employee,
            ApprovalDecisionType.Approved,
            rejectionReason: null,
            transfer: null) with
        {
            TotalAmountRials = amountRials
        };

        using var workbook = Open(new ExcelWorkbookBuilder().BuildIndividual(report, GeneratedAt));

        Assert.Contains(expected, CellText(workbook.Worksheet("Decision record")));
    }

    [Theory]
    [InlineData(1, "یک ریال")]
    [InlineData(12_500_000, "دوازده میلیون و پانصد هزار ریال")]
    [InlineData(10_000_000_000, "ده میلیارد ریال")]
    public void PersianIndividualAmountInWords_IsCompleteAndReadable(
        long amountRials,
        string expected)
    {
        var report = CreateIndividualReport(
            RequesterType.Employee,
            ApprovalDecisionType.Approved,
            rejectionReason: null,
            transfer: null) with
        {
            TotalAmountRials = amountRials
        };

        using var workbook = Open(new ExcelWorkbookBuilder().BuildIndividual(
            report,
            GeneratedAt,
            ReportLanguage.Persian));

        Assert.Contains(expected, CellText(workbook.Worksheet("گزارش درخواست")));
    }

    private static ExcelReportService CreateService(ApplicationDbContext dbContext) =>
        new(dbContext, new ExcelWorkbookBuilder(), new FixedTimeProvider(GeneratedAt));

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static PaymentRequest CreateApprovedRequest(
        int id,
        string reference,
        RequesterType requesterType,
        string requesterName,
        string requesterUsername,
        DateTimeOffset decidedAt)
    {
        var request = CreateBaseRequest(id, reference, requesterType);
        request.Status = PaymentRequestStatus.Approved;
        request.RequesterFullNameSnapshot = requesterName;
        request.RequesterUsernameSnapshot = requesterUsername;
        request.Decision = CreateDecision(request, ApprovalDecisionType.Approved, decidedAt);
        request.TransferOutcome = new TransferOutcome
        {
            Id = id,
            PaymentRequestId = id,
            PaymentRequest = request,
            Status = TransferStatus.AwaitingTransfer,
            IsClosed = false
        };
        return request;
    }

    private static PaymentRequest CreateBaseRequest(
        int id,
        string reference,
        RequesterType requesterType) => new()
    {
        Id = id,
        ReferenceNumber = reference,
        RequesterType = requesterType,
        EmployeeRequesterId = requesterType == RequesterType.Employee ? 1000 + id : null,
        ApplicationUserRequesterId = requesterType == RequesterType.Administrator ? 2000 + id : null,
        RequesterFullNameSnapshot = "Requester Snapshot",
        RequesterUsernameSnapshot = $"requester-{id}",
        RequesterPhoneNumberSnapshot = "09120000000",
        RequesterDepartmentSnapshot = "Operations",
        RequesterBranchSnapshot = "Main",
        RequesterJobTitleSnapshot = "Coordinator",
        Justification = "Payment purpose",
        PayeeName = "Payee",
        RequestedAccountOrCardNumber = "001234",
        TotalAmountRials = 1_000_000,
        Status = PaymentRequestStatus.Pending,
        SubmittedAtUtc = new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.Zero),
        TrackingTokenHash = new string('A', 64)
    };

    private static ApprovalDecision CreateDecision(
        PaymentRequest request,
        ApprovalDecisionType type,
        DateTimeOffset decidedAt) => new()
    {
        Id = request.Id,
        PaymentRequestId = request.Id,
        PaymentRequest = request,
        DecisionType = type,
        DecidedByUserId = 77,
        DecidedByFullNameSnapshot = "Decision Admin Snapshot",
        DecidedAtUtc = decidedAt
    };

    private static void AddDuplicateRiskChildren(PaymentRequest request, int idSeed)
    {
        request.Lines.Add(new PaymentRequestLine
        {
            Id = idSeed + 1,
            PaymentRequestId = request.Id,
            PaymentRequest = request,
            DisplayOrder = 0,
            Description = "First",
            AmountRials = 400_000
        });
        request.Lines.Add(new PaymentRequestLine
        {
            Id = idSeed + 2,
            PaymentRequestId = request.Id,
            PaymentRequest = request,
            DisplayOrder = 1,
            Description = "Second",
            AmountRials = 600_000
        });
        request.Attachments.Add(new PaymentRequestAttachment
        {
            Id = idSeed + 3,
            PaymentRequestId = request.Id,
            PaymentRequest = request,
            OriginalFileName = "one.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1,
            Sha256Hash = new string('C', 64),
            Content = [1]
        });
        request.Attachments.Add(new PaymentRequestAttachment
        {
            Id = idSeed + 4,
            PaymentRequestId = request.Id,
            PaymentRequest = request,
            OriginalFileName = "two.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1,
            Sha256Hash = new string('D', 64),
            Content = [2]
        });
    }

    private static IndividualRequestReport CreateIndividualReport(
        RequesterType requesterType,
        ApprovalDecisionType decisionType,
        string? rejectionReason,
        TransferReportSnapshot? transfer) => new(
        1,
        "PR-TEST",
        requesterType,
        "Requester Snapshot",
        "000123",
        "09120000000",
        "Operations",
        "Main",
        "Coordinator",
        "Purpose",
        "Payee",
        "00009999",
        1_000_000,
        decisionType == ApprovalDecisionType.Approved
            ? PaymentRequestStatus.Approved
            : PaymentRequestStatus.Rejected,
        GeneratedAt.AddDays(-1),
        0,
        new DecisionReportSnapshot(
            decisionType,
            7,
            "Decision Admin Snapshot",
            rejectionReason,
            GeneratedAt.AddHours(-2)),
        transfer,
        [new RequestLineReportRow(0, "Expense", 1_000_000, false, null)]);

    private static XLWorkbook Open(byte[] content) => new(new MemoryStream(content));

    private static IReadOnlyList<string> CellText(IXLWorksheet sheet) =>
        sheet.CellsUsed()
            .Where(cell => cell.DataType == XLDataType.Text)
            .Select(cell => cell.GetString())
            .ToList();

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
