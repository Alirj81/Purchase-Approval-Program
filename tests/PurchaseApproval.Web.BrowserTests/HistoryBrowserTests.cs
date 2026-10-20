// Drives the three history views through the real browser with seeded data whose timestamps,
// names and outcomes are known: the administrator company history (search, Persian and English
// text, no-match, date boundaries in a device zone, combined filters, paging, clearing), the
// administrator's own requests and per-requester queue filter, the employee's personal history
// with its payment and closure labels and ownership, and the accountant's execution views.
// Rows are written directly to the test database so dates and outcomes are exact; the pages
// under test are the real ones served by the published application.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;
using PurchaseApproval.Web.IntegrationTests;

namespace PurchaseApproval.Web.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class HistoryBrowserTests(AppServerFixture fixture) : IAsyncLifetime
{
    private const string AdminPassword = "Administrator#Pass123";
    private const string AccountantPassword = "Accountant#Pass1234";
    private const string Zone = "Asia/Tehran";

    // Seeded identities and references, filled by SeedAsync.
    private int _adminId;
    private (int Id, string Username, string AccessKey) _ali;
    private (int Id, string Username, string AccessKey) _john;
    private (int Id, string Username, string AccessKey) _pager;
    private readonly Dictionary<string, int> _ids = new();

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsAvailable)
        {
            return;
        }

        await TestDatabase.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Administrator company history ----------------------------------------------------------

    [BrowserFact]
    public async Task AdminHistory_SearchByReferencePersianAndEnglishNames_NoMatch_AndClear()
    {
        var page = await NewPageAsync();
        await SignInStaffAsync(page, "admin", AdminPassword, "**/admin/requests**");

        // Persian interface first: search by part of a Persian name and check Persian labels.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?AllDates=true&Search=رحیمی&TimeZoneId={Zone}");
        var references = await ReferencesWithPagingAsync(page);
        Assert.Equal(4, references.Count);                    // Ali's four requests across two pages of three, nobody else's
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?AllDates=true&Search=رحیمی&TimeZoneId={Zone}");
        Assert.All(references, reference => Assert.Contains(reference, _ids.Keys.Where(key => key.StartsWith("ALI")).Select(key => Reference(key))));
        var text = await MainTextAsync(page);
        Assert.Contains("علی رحیمی", text);
        Assert.DoesNotContain("John Smith", text);
        Assert.Contains("تأیید شده", text);                    // Approved
        Assert.Contains("رد شده", text);                       // Rejected
        Assert.Contains("پرداخت شده", text);                   // Transferred
        await page.ScreenshotAsync(new() { Path = Shot("history-01-persian-search"), FullPage = true });

        await UseEnglishAsync(page);

        // English name, case-insensitive, with AllDates so the window does not hide anything.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?AllDates=true&Search=smith&TimeZoneId={Zone}");
        references = await ReferencesAsync(page);
        Assert.Equal(2, references.Count);
        Assert.Contains("John Smith", await MainTextAsync(page));

        // Exact reference through the form itself.
        await page.FillAsync("#Search", Reference("ALI-REJECTED"));
        await page.CheckAsync("#AllDates");
        await page.ClickAsync("button:has-text('Apply history filters')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        references = await ReferencesAsync(page);
        Assert.Equal([Reference("ALI-REJECTED")], references);
        Assert.Contains("1 matching records", await MainTextAsync(page));

        // Payee text matches too; a nonsense search matches nothing and says so.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?AllDates=true&Search=Office%20Depot&TimeZoneId={Zone}");
        Assert.Equal([Reference("JOHN-AWAITING")], await ReferencesAsync(page));
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?AllDates=true&Search=zzz-no-such-text&TimeZoneId={Zone}");
        Assert.Empty(await ReferencesAsync(page));
        Assert.Contains("No history records match these filters.", await MainTextAsync(page));

        // Clear returns to the default view: no search, recent window, recent rows only.
        await page.ClickAsync("a:has-text('Clear')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal("", await page.InputValueAsync("#Search"));
        Assert.False(await page.IsCheckedAsync("#AllDates"));
        Assert.Contains("Showing the last 3 calendar months", await MainTextAsync(page));
        Assert.DoesNotContain(Reference("ALI-OLD"), await MainTextAsync(page));
        await page.ScreenshotAsync(new() { Path = Shot("history-02-cleared"), FullPage = true });
    }

    [BrowserFact]
    public async Task AdminHistory_DefaultWindow_AllDates_AndLocalDateBoundaries()
    {
        var page = await NewPageAsync();
        await SignInStaffAsync(page, "admin", AdminPassword, "**/admin/requests**");
        await UseEnglishAsync(page);

        // Default: last three months by submission date; the four-month-old request is hidden.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?TimeZoneId={Zone}");
        var defaultReferences = await ReferencesWithPagingAsync(page);
        Assert.DoesNotContain(Reference("ALI-OLD"), defaultReferences);
        Assert.Contains(Reference("ALI-PENDING"), defaultReferences);

        // All dates lifts the window.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?AllDates=true&TimeZoneId={Zone}&Sort=OldestFirst");
        var all = await ReferencesWithPagingAsync(page);
        Assert.Contains(Reference("ALI-OLD"), all);
        Assert.Equal(Reference("ALI-OLD"), all[0]);          // oldest first

        // Local-date boundary in Asia/Tehran (UTC+03:30): ALI-TRANSFERRED was submitted at
        // 2026-09-14 20:00Z = 23:30 local on the 14th; ALI-REJECTED at 20:30Z = 00:00 local on the 15th.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?Date=2026-09-15&TimeZoneId={Zone}&TimestampField=Submission");
        Assert.Equal([Reference("ALI-REJECTED")], await ReferencesAsync(page));
        Assert.Contains("UTC boundary: 2026-09-14 20:30:00Z to before 2026-09-15 20:30:00Z", await MainTextAsync(page));
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?Date=2026-09-14&TimeZoneId={Zone}&TimestampField=Submission");
        Assert.Equal([Reference("ALI-TRANSFERRED")], await ReferencesAsync(page));

        // The same two requests fall on the same UTC day.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?Date=2026-09-14&TimeZoneId=UTC&TimestampField=Submission&Sort=OldestFirst");
        Assert.Equal([Reference("ALI-TRANSFERRED"), Reference("ALI-REJECTED")], await ReferencesAsync(page));

        // Inclusive From/To range on the decision timestamp: both decided on 2026-09-15 (UTC).
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?From=2026-09-15&To=2026-09-15&TimeZoneId=UTC&TimestampField=Decision&Sort=OldestFirst");
        Assert.Equal([Reference("ALI-TRANSFERRED"), Reference("ALI-REJECTED")], await ReferencesAsync(page));

        // Closure timestamp: only the automatically closed item has one.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?From=2026-09-01&To=2026-09-30&TimeZoneId=UTC&TimestampField=Closure");
        Assert.Equal([Reference("ALI-TRANSFERRED")], await ReferencesAsync(page));

        // A reversed range and a missing timezone are reported, not silently ignored.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?From=2026-09-20&To=2026-09-01&TimeZoneId=UTC");
        Assert.Contains("The From date cannot be later than the To date.", await MainTextAsync(page));
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?Date=2026-09-15&TimeZoneId=");
        Assert.Contains("Enter the device or business timezone", await MainTextAsync(page));
        await page.ScreenshotAsync(new() { Path = Shot("history-03-date-boundary"), FullPage = true });
    }

    [BrowserFact]
    public async Task AdminHistory_CombinedFilters_Labels_RequesterFilter_AndPaging()
    {
        var page = await NewPageAsync();
        await SignInStaffAsync(page, "admin", AdminPassword, "**/admin/requests**");
        await UseEnglishAsync(page);
        var all = $"AllDates=true&TimeZoneId={Zone}";

        // Approved + Unsuccessful: one row, with its failure reason and Open state.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&ApprovalStatus=Approved&ExecutionOutcome=Unsuccessful");
        Assert.Equal([Reference("JOHN-UNSUCCESSFUL")], await ReferencesAsync(page));
        var text = await MainTextAsync(page);
        Assert.Contains("Unsuccessful", text);
        Assert.Contains("Card declined", text);
        Assert.Contains("Open", text);

        // Closed only: the transferred item, with the automatic closure reason.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&Closure=Closed");
        Assert.Equal([Reference("ALI-TRANSFERRED")], await ReferencesAsync(page));
        text = await MainTextAsync(page);
        Assert.Contains("Closed", text);
        Assert.Contains("Automatic", text);
        Assert.Contains(TransferExecutionRules.AutomaticTransferredClosureReason, text);

        // Awaiting payment record (approved, no outcome yet) and the self-approved request.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&ExecutionOutcome=AwaitingTransfer");
        Assert.Equal(new[] { Reference("JOHN-AWAITING"), Reference("ADMIN-SELF") }.OrderBy(r => r).ToList(), (await ReferencesAsync(page)).OrderBy(r => r).ToList());
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&SelfApprovedOnly=true");
        Assert.Equal([Reference("ADMIN-SELF")], await ReferencesAsync(page));
        Assert.Contains("Self-approved", await MainTextAsync(page));

        // Contradictory filters give no rows, not an error.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&ApprovalStatus=Rejected&Closure=Closed");
        Assert.Empty(await ReferencesAsync(page));
        Assert.Contains("No history records match these filters.", await MainTextAsync(page));

        // Per-requester history: John's two requests; the same ID for the other requester type is a different person.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&RequesterType=Employee&RequesterId={_john.Id}");
        Assert.Equal(2, (await ReferencesAsync(page)).Count);
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&RequesterType=Administrator&RequesterId={_adminId}");
        Assert.Equal([Reference("ADMIN-SELF")], await ReferencesAsync(page));
        // A requester ID without a requester type cannot be interpreted and must be reported.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&RequesterId={_adminId}");
        Assert.Contains("Choose the requester type", await MainTextAsync(page));

        // Paging (page size 3 in this host): the search survives the Next link and pages do not overlap.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history?{all}&Search=PR-&Sort=OldestFirst");
        var first = await ReferencesAsync(page);
        Assert.Equal(3, first.Count);
        Assert.Contains("Page 1 of", await MainTextAsync(page));
        await page.ClickAsync("a:has-text('Next page')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Contains("Search=PR-", page.Url);
        Assert.Contains("pageNumber=2", page.Url);
        var second = await ReferencesAsync(page);
        Assert.Equal(3, second.Count);
        Assert.Empty(first.Intersect(second));
        await page.ScreenshotAsync(new() { Path = Shot("history-04-page-2"), FullPage = true });
    }

    // --- Administrator: own requests and the per-requester queue filter -------------------------

    [BrowserFact]
    public async Task AdminQueue_MyRequests_StatusTabs_AndPerRequesterFilter()
    {
        var page = await NewPageAsync();
        await SignInStaffAsync(page, "admin", AdminPassword, "**/admin/requests**");
        await UseEnglishAsync(page);

        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/requests?status=all&mine=true");
        Assert.Equal([Reference("ADMIN-SELF")], await ReferencesAsync(page));
        Assert.Contains("My submitted requests", await MainTextAsync(page));
        Assert.Contains("Self-approved", await MainTextAsync(page));

        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/requests?status=rejected");
        Assert.Equal([Reference("ALI-REJECTED")], await ReferencesAsync(page));

        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/requests?status=all&requesterType=Employee&requesterId={_ali.Id}");
        Assert.Equal(4, (await ReferencesAsync(page)).Count);
        Assert.Contains("Requests from the selected requester", await MainTextAsync(page));

        // Pending tab with paging: the pager employee's 26 pending requests span two pages of 25.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/requests?status=pending&requesterType=Employee&requesterId={_pager.Id}");
        Assert.Equal(25, (await ReferencesAsync(page)).Count);
        await page.ClickAsync("a:has-text('Next page')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Contains("requesterId=", page.Url);
        Assert.Single(await ReferencesAsync(page));
    }

    // --- Employee personal history --------------------------------------------------------------

    [BrowserFact]
    public async Task EmployeeHistory_ShowsOnlyOwnRequests_WithPaymentAndClosureLabels_AndBlocksOthers()
    {
        var page = await NewPageAsync();
        await SignInEmployeeAsync(page, _ali.AccessKey);
        await UseEnglishAsync(page);

        await page.GotoAsync($"{AppServerFixture.BaseUrl}/employee/requests");
        var references = await ReferencesAsync(page);
        Assert.Equal(4, references.Count);
        Assert.DoesNotContain(Reference("JOHN-AWAITING"), references);
        var text = await MainTextAsync(page);
        if (!text.Contains("Payment recorded"))
        {
            // Keep the page for diagnosis when the label is missing.
            await File.WriteAllTextAsync(Shot("history-05-employee").Replace(".png", ".html"), await page.ContentAsync());
        }
        Assert.Contains("Payment recorded", text);
        Assert.Contains("Execution item closed", text);
        Assert.Contains("Rejected", text);
        Assert.Contains("Pending", text);
        Assert.DoesNotContain("John Smith", text);
        await page.ScreenshotAsync(new() { Path = Shot("history-05-employee"), FullPage = true });

        // Own detail page shows the requester-visible explanation; another employee's request is not found.
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/employee/requests/{_ids["ALI-REJECTED"]}");
        Assert.Contains("فاکتور ناقص است", await MainTextAsync(page));
        var foreign = await page.GotoAsync($"{AppServerFixture.BaseUrl}/employee/requests/{_ids["JOHN-AWAITING"]}");
        Assert.Equal(404, foreign!.Status);
        var foreignAttachment = await page.GotoAsync($"{AppServerFixture.BaseUrl}/employee/requests/{_ids["JOHN-AWAITING"]}?handler=Attachment&attachmentId=1");
        Assert.Equal(404, foreignAttachment!.Status);

        // John sees his two with the requester-facing labels for awaiting and unsuccessful.
        await SignOutAsync(page, "form[action*='employee/logout'] button");
        await SignInEmployeeAsync(page, _john.AccessKey);
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/employee/requests");
        text = await MainTextAsync(page);
        Assert.Equal(2, (await ReferencesAsync(page)).Count);
        Assert.Contains("Awaiting payment record", text);
        Assert.Contains("Payment attempt unsuccessful", text);
        Assert.DoesNotContain("Card declined", text);             // the reason is for staff, not the employee list

        // Paging for the employee with 26 requests: two pages, 25 + 1, no overlap.
        await SignOutAsync(page, "form[action*='employee/logout'] button");
        await SignInEmployeeAsync(page, _pager.AccessKey);
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/employee/requests");
        var pageOne = await ReferencesAsync(page);
        Assert.Equal(25, pageOne.Count);
        await page.ClickAsync("a:has-text('Next page')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var pageTwo = await ReferencesAsync(page);
        Assert.Single(pageTwo);
        Assert.Empty(pageOne.Intersect(pageTwo));
    }

    // --- Accountant execution views -------------------------------------------------------------

    [BrowserFact]
    public async Task ExecutionViews_OpenHistoryAndAll_KeepOutcomeAndClosureLabels()
    {
        var page = await NewPageAsync();
        await SignInStaffAsync(page, "acct", AccountantPassword, "**/execution**");
        await UseEnglishAsync(page);

        await page.GotoAsync($"{AppServerFixture.BaseUrl}/execution?status=open");
        var open = await ReferencesAsync(page);
        Assert.Equal(new[] { Reference("ADMIN-SELF"), Reference("JOHN-AWAITING"), Reference("JOHN-UNSUCCESSFUL") }.OrderBy(r => r), open.OrderBy(r => r));
        var text = await MainTextAsync(page);
        Assert.Contains("Unsuccessful; awaiting 48-hour automatic closure", text);
        Assert.Contains("Awaiting payment record", text);

        await page.GotoAsync($"{AppServerFixture.BaseUrl}/execution?status=history");
        Assert.Equal([Reference("ALI-TRANSFERRED")], await ReferencesAsync(page));
        text = await MainTextAsync(page);
        Assert.Contains("Closed", text);
        Assert.Contains("Transferred", text);

        await page.GotoAsync($"{AppServerFixture.BaseUrl}/execution?status=all");
        Assert.Equal(4, (await ReferencesAsync(page)).Count);

        // The accountant has no company history or review queue.
        var history = await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/history");
        Assert.Contains("/account/access-denied", page.Url);
        await page.ScreenshotAsync(new() { Path = Shot("history-06-execution"), FullPage = true });
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private async Task SeedAsync()
    {
        _adminId = await fixture.Seed.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword, "مینا مدیر");
        await fixture.Seed.CreateStaffAsync("acct", AppRoles.Accountant, AccountantPassword, "امیر حسابدار");
        _ali = await fixture.Seed.CreateEmployeeAsync("ali.rahimi", "علی رحیمی");
        _john = await fixture.Seed.CreateEmployeeAsync("john.smith", "John Smith");
        _pager = await fixture.Seed.CreateEmployeeAsync("pager.employee", "Pager Employee");

        var at = (string value) => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.AssumeUniversal);
        _ids["ALI-TRANSFERRED"] = await SubmitAsync(_ali, "فروشگاه کاغذ", at("2026-09-14T20:00:00Z"));
        _ids["ALI-REJECTED"] = await SubmitAsync(_ali, "Printer Shop", at("2026-09-14T20:30:00Z"));
        _ids["ALI-PENDING"] = await SubmitAsync(_ali, "Local Courier", at("2026-10-10T10:00:00Z"));
        _ids["ALI-OLD"] = await SubmitAsync(_ali, "Old Supplier", at("2026-06-01T10:00:00Z"));
        _ids["JOHN-AWAITING"] = await SubmitAsync(_john, "Office Depot", at("2026-10-01T10:00:00Z"));
        _ids["JOHN-UNSUCCESSFUL"] = await SubmitAsync(_john, "Cable Company", at("2026-10-05T10:00:00Z"));
        _ids["ADMIN-SELF"] = await SubmitAdministratorAsync(_adminId, "مینا مدیر", "admin", "Admin Supplier", at("2026-10-08T10:00:00Z"));
        for (var i = 1; i <= 26; i++)
        {
            await SubmitAsync(_pager, $"Pager payee {i}", at("2026-10-12T10:00:00Z").AddMinutes(i));
        }

        await using var context = TestDatabase.CreateContext();
        // ALI-TRANSFERRED: approved on the 15th, transferred, closed automatically 24 h later.
        await DecideAsync(context, _ids["ALI-TRANSFERRED"], ApprovalDecisionType.Approved, null, at("2026-09-15T08:00:00Z"), false);
        context.TransferOutcomes.Add(Outcome(_ids["ALI-TRANSFERRED"], TransferStatus.Transferred, at("2026-09-15T09:00:00Z"), null, closedAt: at("2026-09-16T09:00:00Z")));
        // ALI-REJECTED: rejected with a requester-visible explanation.
        await DecideAsync(context, _ids["ALI-REJECTED"], ApprovalDecisionType.Rejected, "فاکتور ناقص است", at("2026-09-15T10:00:00Z"), false);
        // JOHN-AWAITING: approved, awaiting the first outcome.
        await DecideAsync(context, _ids["JOHN-AWAITING"], ApprovalDecisionType.Approved, null, at("2026-10-02T10:00:00Z"), false);
        context.TransferOutcomes.Add(new TransferOutcome { PaymentRequestId = _ids["JOHN-AWAITING"], Status = TransferStatus.AwaitingTransfer, IsClosed = false });
        // JOHN-UNSUCCESSFUL: approved, unsuccessful attempt recorded, still open.
        await DecideAsync(context, _ids["JOHN-UNSUCCESSFUL"], ApprovalDecisionType.Approved, null, at("2026-10-05T12:00:00Z"), false);
        context.TransferOutcomes.Add(Outcome(_ids["JOHN-UNSUCCESSFUL"], TransferStatus.Unsuccessful, at("2026-10-06T10:00:00Z"), "Card declined"));
        // ADMIN-SELF: the administrator approved their own request; awaiting the first outcome.
        await DecideAsync(context, _ids["ADMIN-SELF"], ApprovalDecisionType.Approved, null, at("2026-10-08T11:00:00Z"), true);
        context.TransferOutcomes.Add(new TransferOutcome { PaymentRequestId = _ids["ADMIN-SELF"], Status = TransferStatus.AwaitingTransfer, IsClosed = false });
        await context.SaveChangesAsync();
    }

    private async Task<int> SubmitAsync((int Id, string Username, string AccessKey) employee, string payee, DateTimeOffset submittedAt)
    {
        int id;
        using (var scope = fixture.Seed.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentRequestSubmissionService>();
            var employeeRow = await scope.ServiceProvider.GetRequiredService<PurchaseApproval.Data.ApplicationDbContext>().Employees.SingleAsync(e => e.Id == employee.Id);
            var result = await service.SubmitAsync(
                new EmployeeProfile(employee.Id, employeeRow.FullName, employee.Username, null, null, null, null),
                [new PaymentRequestLineInput("Row", 1_000_000, false, null)], [], payee, "Reason", null,
                PaymentRequestSubmissionRules.CreateSubmissionKey());
            id = result.RequestId;
        }

        await SetSubmittedAtAsync(id, submittedAt);
        return id;
    }

    private async Task<int> SubmitAdministratorAsync(int adminId, string fullName, string username, string payee, DateTimeOffset submittedAt)
    {
        int id;
        using (var scope = fixture.Seed.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IPaymentRequestSubmissionService>();
            var result = await service.SubmitAsync(
                new ApplicationUserProfile(adminId, fullName, username, null, null, null, null),
                [new PaymentRequestLineInput("Row", 2_000_000, false, null)], [], payee, "Reason", null,
                PaymentRequestSubmissionRules.CreateSubmissionKey());
            id = result.RequestId;
        }

        await SetSubmittedAtAsync(id, submittedAt);
        return id;
    }

    private static async Task SetSubmittedAtAsync(int requestId, DateTimeOffset submittedAt)
    {
        await using var context = TestDatabase.CreateContext();
        await context.PaymentRequests.Where(item => item.Id == requestId)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.SubmittedAtUtc, submittedAt));
    }

    private async Task DecideAsync(PurchaseApproval.Data.ApplicationDbContext context, int requestId, ApprovalDecisionType type, string? reason, DateTimeOffset decidedAt, bool selfApproval)
    {
        var request = await context.PaymentRequests.SingleAsync(item => item.Id == requestId);
        request.Status = type == ApprovalDecisionType.Approved ? PaymentRequestStatus.Approved : PaymentRequestStatus.Rejected;
        context.ApprovalDecisions.Add(new ApprovalDecision
        {
            PaymentRequestId = requestId,
            DecisionType = type,
            DecidedByUserId = _adminId,
            DecidedByFullNameSnapshot = "مینا مدیر",
            RejectionReason = reason,
            DecidedAtUtc = decidedAt,
            IsSelfApproval = selfApproval
        });
    }

    private TransferOutcome Outcome(int requestId, TransferStatus status, DateTimeOffset recordedAt, string? failureReason, DateTimeOffset? closedAt = null)
    {
        var outcome = new TransferOutcome
        {
            PaymentRequestId = requestId,
            Status = status,
            Method = TransferMethod.Cash,
            PerformedByUserId = _adminId,
            PerformedByFullNameSnapshot = "مینا مدیر",
            PerformedByRoleSnapshot = AppRoles.Administrator,
            TransactionOccurredAtUtc = recordedAt.AddMinutes(-5),
            TransactionUtcOffsetMinutes = 210,
            TransactionTimeZoneId = Zone,
            RecordedAtUtc = recordedAt,
            FailureReason = failureReason,
            IsClosed = false
        };
        if (closedAt is { } closed)
        {
            outcome.IsClosed = true;
            outcome.ClosedAtUtc = closed;
            outcome.ClosureType = TransferClosureType.Automatic;
            outcome.ClosureReason = status == TransferStatus.Transferred
                ? TransferExecutionRules.AutomaticTransferredClosureReason
                : TransferExecutionRules.AutomaticUnsuccessfulClosureReason;
        }

        return outcome;
    }

    // --- Browser helpers -------------------------------------------------------------------------

    private async Task<IPage> NewPageAsync()
    {
        var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = WorkflowBrowserTests.Desktop,
            Locale = "fa-IR",
            TimezoneId = Zone
        });
        return await context.NewPageAsync();
    }

    private static async Task SignInStaffAsync(IPage page, string username, string password, string expectedUrl)
    {
        await page.GotoAsync(AppServerFixture.BaseUrl + "/account/login");
        await page.FillAsync("#Input_Username", username);
        await page.FillAsync("#Input_Password", password);
        await page.ClickAsync("#main-content button[type=submit]");
        await page.WaitForURLAsync(expectedUrl, new() { Timeout = 15_000 });
    }

    private static async Task SignInEmployeeAsync(IPage page, string accessKey)
    {
        await page.GotoAsync(AppServerFixture.BaseUrl + "/");
        await page.FillAsync("#Input_AccessKey", accessKey);
        await page.ClickAsync("#main-content button[type=submit]");
        await page.WaitForURLAsync("**/employee", new() { Timeout = 15_000 });
    }

    private static async Task SignOutAsync(IPage page, string buttonSelector)
    {
        var button = page.Locator(buttonSelector).First;
        if (!await button.IsVisibleAsync())
        {
            await page.ClickAsync(".navbar-toggler");
            await page.WaitForSelectorAsync("#main-navigation.show");
        }

        await button.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task UseEnglishAsync(IPage page)
    {
        if (await page.GetAttributeAsync("html", "dir") == "rtl")
        {
            await page.ClickAsync("button:has-text('English')");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }

    /// <summary>Reference numbers of the rows currently shown, in page order.</summary>
    private static async Task<List<string>> ReferencesAsync(IPage page)
    {
        var references = await page.Locator("main table tbody tr .reference").AllTextContentsAsync();
        return references.Select(value => value.Trim()).ToList();
    }

    /// <summary>References across every page of the current result set.</summary>
    private static async Task<List<string>> ReferencesWithPagingAsync(IPage page)
    {
        // Follows the pager by its link target, so it works in Persian and English alike.
        var all = new List<string>(await ReferencesAsync(page));
        for (var next = 2; await page.Locator($"nav a[href*='pageNumber={next}']").CountAsync() > 0; next++)
        {
            await page.ClickAsync($"nav a[href*='pageNumber={next}']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            all.AddRange(await ReferencesAsync(page));
        }

        return all;
    }

    private static async Task<string> MainTextAsync(IPage page) => await page.TextContentAsync("main") ?? string.Empty;

    private string Reference(string key)
    {
        using var context = TestDatabase.CreateContext();
        return context.PaymentRequests.Where(item => item.Id == _ids[key]).Select(item => item.ReferenceNumber).Single();
    }

    private static string Shot(string name) => Path.Combine(AppServerFixture.ScreenshotDirectory, $"{name}.png");
}
