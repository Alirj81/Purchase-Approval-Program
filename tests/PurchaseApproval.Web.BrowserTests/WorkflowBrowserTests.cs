// Drives the complete employee -> administrator -> accountant workflow in a real browser at a
// desktop and a phone viewport, in the Persian default interface, and records screenshots.
// These are browser-viewport checks; they are not physical-device checks.
using Microsoft.Playwright;
using PurchaseApproval.Security;
using PurchaseApproval.Web.IntegrationTests;

namespace PurchaseApproval.Web.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class WorkflowBrowserTests(AppServerFixture fixture) : IAsyncLifetime
{
    private const string AdminPassword = "Administrator#Pass123";
    private const string AccountantPassword = "Accountant#Pass1234";

    public static readonly ViewportSize Desktop = new() { Width = 1280, Height = 800 };
    public static readonly ViewportSize Phone = new() { Width = 390, Height = 844 };

    public async Task InitializeAsync()
    {
        if (TestDatabase.IsAvailable)
        {
            await TestDatabase.ResetAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [BrowserFact]
    public async Task Desktop_CompleteWorkflow_InPersian()
    {
        await RunWorkflowAsync(Desktop, "desktop");
    }

    [BrowserFact]
    public async Task Phone_CompleteWorkflow_InPersian()
    {
        await RunWorkflowAsync(Phone, "phone");
    }

    [BrowserFact]
    public async Task LanguageSwitch_TogglesDirection()
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = Desktop });
        var page = await context.NewPageAsync();
        await page.GotoAsync(AppServerFixture.BaseUrl + "/");
        Assert.Equal("rtl", await page.GetAttributeAsync("html", "dir"));
        await page.ClickAsync("button:has-text('English')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal("ltr", await page.GetAttributeAsync("html", "dir"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(AppServerFixture.ScreenshotDirectory, "desktop-login-english.png"), FullPage = true });
        await page.ClickAsync("button:has-text('فارسی')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal("rtl", await page.GetAttributeAsync("html", "dir"));
    }

    private async Task RunWorkflowAsync(ViewportSize viewport, string prefix)
    {
        await fixture.Seed.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword, "مینا مدیر");
        await fixture.Seed.CreateStaffAsync("acct", AppRoles.Accountant, AccountantPassword, "امیر حسابدار");
        var employee = await fixture.Seed.CreateEmployeeAsync("employee.one", "علی رحیمی");

        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = viewport,
            Locale = "fa-IR",
            TimezoneId = "Asia/Tehran"
        });
        var page = await context.NewPageAsync();
        string Shot(string name) => Path.Combine(AppServerFixture.ScreenshotDirectory, $"{prefix}-{name}.png");

        // Browser console errors are collected so a failing step can be diagnosed from the report.
        var consoleMessages = new List<string>();
        page.Console += (_, message) => consoleMessages.Add($"[{message.Type}] {message.Text}");
        page.PageError += (_, error) => consoleMessages.Add($"[pageerror] {error}");
        try
        {
            await RunStepsAsync(page, employee, Shot);
        }
        catch (Exception)
        {
            await page.ScreenshotAsync(new() { Path = Shot("ERROR"), FullPage = true });
            await File.WriteAllTextAsync(Shot("ERROR").Replace(".png", ".html"), await page.ContentAsync());
            await File.WriteAllLinesAsync(Shot("ERROR").Replace(".png", ".console.txt"), consoleMessages);
            throw;
        }
    }

    private async Task RunStepsAsync(IPage page, (int Id, string Username, string AccessKey) employee, Func<string, string> Shot)
    {
        // Employee signs in and submits a request with two rows.
        await page.GotoAsync(AppServerFixture.BaseUrl + "/");
        Assert.Equal("rtl", await page.GetAttributeAsync("html", "dir"));
        await page.ScreenshotAsync(new() { Path = Shot("01-employee-login"), FullPage = true });
        await page.FillAsync("#Input_Username", employee.Username);
        await page.FillAsync("#Input_AccessKey", employee.AccessKey);
        await page.ClickAsync("#main-content button[type=submit]");
        try
        {
            await page.WaitForURLAsync("**/employee", new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            // Keep evidence of what the browser actually showed before failing the test.
            await page.ScreenshotAsync(new() { Path = Shot("ERROR-after-login"), FullPage = true });
            await File.WriteAllTextAsync(Shot("ERROR-after-login").Replace(".png", ".html"), await page.ContentAsync());
            throw;
        }

        await page.GotoAsync(AppServerFixture.BaseUrl + "/employee/requests/create");
        await page.FillAsync("#Input_PayeeName", "فروشگاه کاغذ تهران");
        await page.FillAsync("#Input_Justification", "خرید کاغذ و جوهر چاپگر برای واحد اداری");
        await page.FillAsync("#line-description-0", "کاغذ A4");
        await page.FillAsync("#line-amountrials-0", "1500000");
        await page.ClickAsync("#add-line");
        await page.FillAsync("#line-description-1", "جوهر چاپگر");
        await page.FillAsync("#line-amountrials-1", "2250000");
        Assert.Equal("3,750,000", await page.TextContentAsync("#request-total"));
        await page.ScreenshotAsync(new() { Path = Shot("02-employee-form"), FullPage = true });
        await page.ClickAsync("#submit-request");
        await page.WaitForURLAsync("**/employee/requests/submitted/*");
        await page.ScreenshotAsync(new() { Path = Shot("03-employee-receipt"), FullPage = true });
        var requestId = int.Parse(page.Url.Split('/').Last());

        await page.GotoAsync(AppServerFixture.BaseUrl + "/employee/requests");
        Assert.Contains("در انتظار", await page.TextContentAsync("main") ?? string.Empty);
        await page.ScreenshotAsync(new() { Path = Shot("04-employee-history"), FullPage = true });
        await SignOutAsync(page, "form[action*='employee/logout'] button");

        // Administrator reviews and approves through the confirmation dialog.
        await page.GotoAsync(AppServerFixture.BaseUrl + "/account/login");
        await page.FillAsync("#Input_Username", "admin");
        await page.FillAsync("#Input_Password", AdminPassword);
        await page.ClickAsync("#main-content button[type=submit]");
        await page.WaitForURLAsync("**/admin/requests**");
        await page.ScreenshotAsync(new() { Path = Shot("05-admin-queue"), FullPage = true });
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/admin/requests/{requestId}");
        await page.ScreenshotAsync(new() { Path = Shot("06-admin-review"), FullPage = true });
        await page.ClickAsync("button[data-bs-target='#approve-confirmation']");
        await page.WaitForSelectorAsync("#approve-confirmation.show");
        await page.ScreenshotAsync(new() { Path = Shot("07-admin-confirm"), FullPage = false });
        await page.ClickAsync("#approve-confirmation form button[type=submit]");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var reviewText = await page.TextContentAsync("main") ?? string.Empty;
        Assert.Contains("تأیید", reviewText);
        await page.ScreenshotAsync(new() { Path = Shot("08-admin-approved"), FullPage = true });
        await SignOutAsync(page, "form[action*='account/logout'] button");

        // Accountant records the transfer; the zone is detected from the browser context.
        await page.GotoAsync(AppServerFixture.BaseUrl + "/account/login");
        await page.FillAsync("#Input_Username", "acct");
        await page.FillAsync("#Input_Password", AccountantPassword);
        await page.ClickAsync("#main-content button[type=submit]");
        await page.WaitForURLAsync("**/execution**");
        await page.GotoAsync($"{AppServerFixture.BaseUrl}/execution/{requestId}");
        Assert.Equal("Asia/Tehran", await page.InputValueAsync("#transaction-time-zone"));
        await page.SelectOptionAsync("#transfer-method", "2");
        await page.FillAsync("#Input_DestinationAccountOrCardNumber", "6037991234567890");
        await page.FillAsync("#Input_BankName", "بانک ملی");
        await page.ScreenshotAsync(new() { Path = Shot("09-accountant-form"), FullPage = true });
        await page.ClickAsync("button[formaction*='handler=Transferred'], form#outcome-form button.btn-success");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var executionText = await page.TextContentAsync("main") ?? string.Empty;
        Assert.Contains("Asia/Tehran", executionText);
        await page.ScreenshotAsync(new() { Path = Shot("10-accountant-recorded"), FullPage = true });
    }

    /// <summary>At phone width the navigation is collapsed, so the menu is opened before signing out.</summary>
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
}
