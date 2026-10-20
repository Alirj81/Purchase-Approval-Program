// Regression tests for a defect found while driving the history pages in a browser: the pager
// used "page" as its query parameter, but Razor Pages reserves the route value "page" for the
// page path ("/Admin/History/Index"). The administrator history therefore failed validation on
// every request ("The value '/Admin/History/Index' is not valid for PageNumber") and showed no
// rows, and the queue, employee history and execution pages always stayed on page 1. The pager
// now uses "pageNumber". These tests pin the behaviour at the HTTP level.
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PurchaseApproval.Security;
using PurchaseApproval.Services;
using PurchaseApproval.Web.Pages;

namespace PurchaseApproval.Web.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HistoryPagingTests : IAsyncLifetime
{
    private const string AdminPassword = "Administrator#Pass123";
    private TestWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsAvailable)
        {
            return;
        }

        await TestDatabase.ResetAsync();
        _factory = new TestWebApplicationFactory(new Dictionary<string, string>
        {
            ["Limits:HistoryPageSize"] = "2",
            ["Limits:DailySubmissionQuota"] = "100",
            ["Limits:SubmissionBurstLimit"] = "100"
        });
        _ = _factory.Services;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [PostgresFact]
    public async Task AdminHistory_ShowsRowsWithoutAPageParameter_AndPagesWithPageNumber()
    {
        await _factory.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword);
        var employee = await _factory.CreateEmployeeAsync("history.employee");
        for (var i = 0; i < 3; i++)
        {
            await SubmitAsync(employee, $"Payee {i}");
        }

        var client = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(client);
        await Browser.SignInStaffAsync(client, "admin", AdminPassword);

        var firstPage = await client.GetStringAsync("/admin/history?AllDates=true&TimeZoneId=UTC");
        Assert.DoesNotContain("is not valid for", firstPage);          // the old validation failure
        Assert.DoesNotContain("validation-summary-errors", firstPage);
        Assert.Equal(2, CountReferences(firstPage));
        Assert.Contains("3 matching records", firstPage);
        Assert.Contains($"{PagerModel.QueryParameter}=2", firstPage);   // the Next link

        var secondPage = await client.GetStringAsync($"/admin/history?AllDates=true&TimeZoneId=UTC&{PagerModel.QueryParameter}=2");
        Assert.Equal(1, CountReferences(secondPage));
        Assert.Contains("Page 2 of 2", secondPage);
    }

    [PostgresFact]
    public async Task QueueAndEmployeeHistory_MoveToTheSecondPage()
    {
        await _factory.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword);
        var employee = await _factory.CreateEmployeeAsync("paging.employee");
        for (var i = 0; i < 26; i++)
        {
            await SubmitAsync(employee, $"Payee {i}");
        }

        var admin = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(admin);
        await Browser.SignInStaffAsync(admin, "admin", AdminPassword);
        var queueFirst = await admin.GetStringAsync("/admin/requests?status=pending");
        var queueSecond = await admin.GetStringAsync($"/admin/requests?status=pending&{PagerModel.QueryParameter}=2");
        Assert.Equal(25, CountReferences(queueFirst));
        Assert.Equal(1, CountReferences(queueSecond));
        Assert.Contains("Page 2 of 2", queueSecond);

        var employeeClient = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(employeeClient);
        var signIn = await Browser.SignInEmployeeAsync(employeeClient, employee.AccessKey);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        var historyFirst = await employeeClient.GetStringAsync("/employee/requests");
        var historySecond = await employeeClient.GetStringAsync($"/employee/requests?{PagerModel.QueryParameter}=2");
        Assert.Equal(25, CountReferences(historyFirst));
        Assert.Equal(1, CountReferences(historySecond));
    }

    [PostgresFact]
    public async Task AdminHistory_RequesterIdWithoutType_IsReportedInsteadOfGuessed()
    {
        await _factory.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword);
        var client = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(client);
        await Browser.SignInStaffAsync(client, "admin", AdminPassword);

        var html = await client.GetStringAsync("/admin/history?AllDates=true&TimeZoneId=UTC&RequesterId=1");
        Assert.Contains("Choose the requester type when filtering by requester ID.", html);
    }

    [PostgresFact]
    public async Task LanguageSwitch_PersistsForASignedInEmployee()
    {
        var employee = await _factory.CreateEmployeeAsync("language.employee");
        var client = _factory.CreateCookieClient();
        var signIn = await Browser.SignInEmployeeAsync(client, employee.AccessKey);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);

        var dashboard = await client.GetStringAsync("/employee");
        Assert.Contains("lang=\"fa\" dir=\"rtl\"", dashboard);
        var switched = await Browser.PostFormAsync(client, "/language",
            [new("code", "en"), new("returnUrl", "/employee")], Browser.ExtractToken(dashboard));
        Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);

        var history = await client.GetStringAsync("/employee/requests");
        Assert.Contains("lang=\"en\" dir=\"ltr\"", history);
    }

    private async Task SubmitAsync((int Id, string Username, string AccessKey) employee, string payee)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentRequestSubmissionService>();
        await service.SubmitAsync(
            new EmployeeProfile(employee.Id, "Paging Employee", employee.Username, null, null, null, null),
            [new PaymentRequestLineInput("Row", 1_000, false, null)], [], payee, "Reason", null,
            PaymentRequestSubmissionRules.CreateSubmissionKey());
    }

    private static int CountReferences(string html) =>
        System.Text.RegularExpressions.Regex.Matches(html, "class=\"reference\"").Count;
}
