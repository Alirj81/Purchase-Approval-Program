// HTTP-level tests through the real pipeline: authentication schemes, authorization policies,
// anti-forgery, ownership isolation, revocation, temporary passwords, throttling, tracking
// redaction and the full employee -> administrator -> accountant -> closure workflow.
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HttpWorkflowTests : IAsyncLifetime
{
    private const string AdminPassword = "Administrator#Pass123";
    private const string AccountantPassword = "Accountant#Pass1234";
    private static readonly byte[] TinyPdf = "%PDF-1.7 integration test"u8.ToArray();

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
            ["Limits:LoginThrottling:FailuresPerAccount"] = "3",
            ["Limits:LoginThrottling:FailuresPerIp"] = "50",
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
    public async Task AnonymousAndWrongRoleRequests_AreRedirectedOrDenied()
    {
        await _factory.CreateStaffAsync("acct", AppRoles.Accountant, AccountantPassword);
        var anonymous = _factory.CreateCookieClient();
        var accountant = _factory.CreateCookieClient();
        await Browser.SignInStaffAsync(accountant, "acct", AccountantPassword);

        var anonymousAdmin = await anonymous.GetAsync("/admin/requests");
        var anonymousExecution = await anonymous.GetAsync("/execution");
        var accountantAdmin = await accountant.GetAsync("/admin/requests");
        var accountantEmployees = await accountant.GetAsync("/Admin/Employees");
        var accountantStaff = await accountant.GetAsync("/admin/staff");
        var accountantReports = await accountant.GetAsync("/admin/reports");
        var accountantExecution = await accountant.GetAsync("/execution");

        Assert.Equal(HttpStatusCode.Redirect, anonymousAdmin.StatusCode);
        Assert.Contains("/account/login", anonymousAdmin.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, anonymousExecution.StatusCode);
        foreach (var denied in new[] { accountantAdmin, accountantEmployees, accountantStaff, accountantReports })
        {
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/account/access-denied", denied.Headers.Location!.ToString());
        }

        Assert.Equal(HttpStatusCode.OK, accountantExecution.StatusCode);
    }

    [PostgresFact]
    public async Task EmployeeSignIn_RequiresMatchingUsernameAndKey_AndThrottlesRepeatedFailures()
    {
        var employee = await _factory.CreateEmployeeAsync("sign.in");
        var other = await _factory.CreateEmployeeAsync("someone.else");
        var client = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(client);

        // Right key with the wrong username, then two wrong keys with the right username.
        var wrongUsername = await Browser.SignInEmployeeAsync(client, employee.Username, other.AccessKey);
        Assert.Equal(HttpStatusCode.OK, wrongUsername.StatusCode);
        Assert.Contains("username or access key is incorrect", await wrongUsername.Content.ReadAsStringAsync());
        var wrongKey = await Browser.SignInEmployeeAsync(client, employee.Username, "pak_1_wrong");
        Assert.Equal(HttpStatusCode.OK, wrongKey.StatusCode);
        var third = await Browser.SignInEmployeeAsync(client, employee.Username, "pak_2_wrong");
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);

        // Three failures for this username reached the configured limit: even the right key is now blocked.
        var blocked = await Browser.SignInEmployeeAsync(client, employee.Username, employee.AccessKey);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("Too many sign-in attempts", await blocked.Content.ReadAsStringAsync());

        // A different account from the same address is unaffected.
        var fresh = _factory.CreateCookieClient();
        var success = await Browser.SignInEmployeeAsync(fresh, other.Username, other.AccessKey);
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        Assert.Equal("/employee", success.Headers.Location!.ToString());
    }

    [PostgresFact]
    public async Task Employees_CannotReadEachOthersRequests_AndTrackingPageIsMinimal()
    {
        var alice = await _factory.CreateEmployeeAsync("alice");
        var bob = await _factory.CreateEmployeeAsync("bob");
        var aliceClient = _factory.CreateCookieClient();
        var bobClient = _factory.CreateCookieClient();
        await Browser.SignInEmployeeAsync(aliceClient, alice.Username, alice.AccessKey);
        await Browser.SignInEmployeeAsync(bobClient, bob.Username, bob.AccessKey);

        var submittedUrl = await Browser.SubmitRequestAsync(aliceClient, "/employee/requests/create", "Alice Supplier", 750_000, TinyPdf);
        var receipt = await aliceClient.GetStringAsync(submittedUrl);
        var trackingUrl = System.Text.RegularExpressions.Regex.Match(receipt, "value=\"(http[^\"]+/track/[^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(trackingUrl));
        var requestId = int.Parse(submittedUrl.Split('/').Last());

        var bobDetails = await bobClient.GetAsync($"/employee/requests/{requestId}");
        var bobAttachment = await bobClient.GetAsync($"/employee/requests/{requestId}?handler=Attachment&attachmentId=1");
        var aliceDetails = await aliceClient.GetAsync($"/employee/requests/{requestId}");
        Assert.Equal(HttpStatusCode.NotFound, bobDetails.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bobAttachment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aliceDetails.StatusCode);
        Assert.Contains("750,000", await aliceDetails.Content.ReadAsStringAsync());

        var anonymous = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var tracking = await anonymous.GetAsync(new Uri(trackingUrl).PathAndQuery);
        var trackingHtml = await tracking.Content.ReadAsStringAsync();
        var wrongToken = await anonymous.GetAsync(new Uri(trackingUrl).PathAndQuery[..^4] + "zzzz");
        Assert.Equal(HttpStatusCode.OK, tracking.StatusCode);
        Assert.DoesNotContain("750,000", trackingHtml);
        Assert.DoesNotContain("Alice Supplier", trackingHtml);
        Assert.DoesNotContain("evidence.pdf", trackingHtml);
        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
    }

    [PostgresFact]
    public async Task RevokedKey_EndsTheSessionBeforeTheNextProtectedAction()
    {
        var employee = await _factory.CreateEmployeeAsync("revoke.me");
        var client = _factory.CreateCookieClient();
        await Browser.SignInEmployeeAsync(client, employee.Username, employee.AccessKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/employee/requests")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<IEmployeeAccessService>();
            await access.RevokeAsync(employee.Id, new AuditActor(null, "Test", "System"));
        }

        var afterRevocation = await client.GetAsync("/employee/requests");
        Assert.Equal(HttpStatusCode.Redirect, afterRevocation.StatusCode);
        Assert.StartsWith("/?", Browser.LocationPath(afterRevocation));
        var signInAgain = await Browser.SignInEmployeeAsync(client, employee.Username, employee.AccessKey);
        Assert.Equal(HttpStatusCode.OK, signInAgain.StatusCode); // refused: the key is revoked
    }

    [PostgresFact]
    public async Task ForgedDecisionPost_WithoutAntiForgeryToken_IsRejected()
    {
        await _factory.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword);
        var employee = await _factory.CreateEmployeeAsync("forge.employee");
        var employeeClient = _factory.CreateCookieClient();
        await Browser.SignInEmployeeAsync(employeeClient, employee.Username, employee.AccessKey);
        var submitted = await Browser.SubmitRequestAsync(employeeClient, "/employee/requests/create", "Payee", 1_000);
        var requestId = int.Parse(submitted.Split('/').Last());

        var admin = _factory.CreateCookieClient();
        await Browser.SignInStaffAsync(admin, "admin", AdminPassword);
        var forged = await admin.PostAsync($"/admin/requests/{requestId}?handler=Approve",
            new FormUrlEncodedContent([new("Input.Version", "0"), new("Input.ApprovalConfirmed", "true")]));

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        await using var context = TestDatabase.CreateContext();
        Assert.Equal(PaymentRequestStatus.Pending, (await context.PaymentRequests.SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task FullWorkflow_SubmitApproveExecuteCloseAndExport()
    {
        await _factory.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword, "Mina Manager");
        await _factory.CreateStaffAsync("acct", AppRoles.Accountant, AccountantPassword, "Amir Accountant");
        var employee = await _factory.CreateEmployeeAsync("flow.employee", "Flow Employee");

        var employeeClient = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(employeeClient);
        await Browser.SignInEmployeeAsync(employeeClient, employee.Username, employee.AccessKey);
        var submitted = await Browser.SubmitRequestAsync(employeeClient, "/employee/requests/create", "Flow Supplier", 2_500_000, TinyPdf);
        var requestId = int.Parse(submitted.Split('/').Last());

        // Administrator approves through the confirmation form.
        var admin = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(admin);
        await Browser.SignInStaffAsync(admin, "admin", AdminPassword);
        var review = await admin.GetStringAsync($"/admin/requests/{requestId}");
        var version = Browser.ExtractHidden(review, "Input.Version");
        var approve = await Browser.PostFormAsync(admin, $"/admin/requests/{requestId}?handler=Approve",
            [new("Input.Version", version), new("Input.ApprovalConfirmed", "true")], Browser.ExtractToken(review));
        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);

        // Cancelling the dialog is a no-op: a second review still shows Pending-only controls? No: already approved.
        var afterApproval = await admin.GetStringAsync($"/admin/requests/{requestId}");
        Assert.Contains("Awaiting payment record", afterApproval);

        // Accountant records a successful transfer with the device zone.
        var accountant = _factory.CreateCookieClient();
        await Browser.UseEnglishAsync(accountant);
        await Browser.SignInStaffAsync(accountant, "acct", AccountantPassword);
        var execution = await accountant.GetStringAsync($"/execution/{requestId}");
        var transferVersion = Browser.ExtractHidden(execution, "Input.Version");
        // The reported time must fall after the approval (seconds ago) and within the 15-minute
        // future tolerance, so "now" in UTC is the only safe value here.
        var occurred = DateTime.UtcNow.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm");
        var record = await Browser.PostFormAsync(accountant, $"/execution/{requestId}?handler=Transferred",
            [
                new("Input.Version", transferVersion),
                new("Input.Method", ((int)TransferMethod.BankTransfer).ToString()),
                new("Input.DestinationAccountOrCardNumber", "6037991234567890"),
                new("Input.BankName", "Test Bank"),
                new("Input.ReportedLocalDateTime", occurred),
                new("Input.TimeZoneId", "UTC"),
                new("Input.ExternalReference", "SEP-0042")
            ], Browser.ExtractToken(execution));
        Assert.True(record.StatusCode == HttpStatusCode.Redirect,
            $"Recording failed: {await Browser.ValidationErrorsAsync(record)}");

        // The accountant cannot close early; the administrator can, with a reason.
        var recorded = await accountant.GetStringAsync($"/execution/{requestId}");
        Assert.DoesNotContain("handler=Close", recorded);
        var adminExecution = await admin.GetStringAsync($"/execution/{requestId}");
        var closeVersion = Browser.ExtractHidden(adminExecution, "CloseInput.Version");
        var closeWithoutReason = await Browser.PostFormAsync(admin, $"/execution/{requestId}?handler=Close",
            [new("CloseInput.Version", closeVersion)], Browser.ExtractToken(adminExecution));
        Assert.Equal(HttpStatusCode.OK, closeWithoutReason.StatusCode);
        Assert.Contains("reason", await closeWithoutReason.Content.ReadAsStringAsync());
        var close = await Browser.PostFormAsync(admin, $"/execution/{requestId}?handler=Close",
            [new("CloseInput.Version", closeVersion), new("CloseInput.Reason", "Confirmed with the payee by phone")],
            Browser.ExtractToken(adminExecution));
        Assert.Equal(HttpStatusCode.Redirect, close.StatusCode);

        // Employee sees decision, payment and closure separately, with the original amount.
        var history = await employeeClient.GetStringAsync("/employee/requests");
        Assert.Contains("Payment recorded", history);
        Assert.Contains("Execution item closed", history);
        Assert.DoesNotContain("6037991234567890", history);
        var details = await employeeClient.GetStringAsync($"/employee/requests/{requestId}");
        Assert.Contains("2,500,000", details);
        Assert.DoesNotContain("Confirmed with the payee", details);

        // Administrator review shows the execution state, and the exports work.
        var finalReview = await admin.GetStringAsync($"/admin/requests/{requestId}");
        Assert.Contains("Transferred", finalReview);
        Assert.Contains("Closed", finalReview);
        var individual = await admin.GetAsync($"/admin/requests/{requestId}?handler=Excel");
        Assert.Equal(HttpStatusCode.OK, individual.StatusCode);
        Assert.Equal(ExcelReportFile.ContentType, individual.Content.Headers.ContentType!.MediaType);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var consolidated = await admin.GetAsync($"/admin/reports?handler=Export&Date={today}&TimeZoneId=UTC");
        Assert.Equal(HttpStatusCode.OK, consolidated.StatusCode);
        Assert.Equal(ExcelReportFile.ContentType, consolidated.Content.Headers.ContentType!.MediaType);
        var accountantExport = await accountant.GetAsync($"/admin/reports?handler=Export&Date={today}&TimeZoneId=UTC");
        Assert.Equal(HttpStatusCode.Redirect, accountantExport.StatusCode);

        await using var context = TestDatabase.CreateContext();
        var outcome = await context.TransferOutcomes.Include(item => item.AuditEntries).SingleAsync();
        Assert.Equal(TransferStatus.Transferred, outcome.Status);
        Assert.True(outcome.IsClosed);
        Assert.Equal("Confirmed with the payee by phone", outcome.ClosureReason);
        Assert.Equal("UTC", outcome.TransactionTimeZoneId);
        Assert.Equal(3, outcome.AuditEntries.Count);
        Assert.Equal(PaymentRequestStatus.Approved, (await context.PaymentRequests.SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task TemporaryPassword_ForcesPasswordChangeBeforeAnyOtherPage()
    {
        var adminId = await _factory.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword);
        string temporary;
        using (var scope = _factory.Services.CreateScope())
        {
            var staff = scope.ServiceProvider.GetRequiredService<IStaffAccountService>();
            var created = await staff.CreateAsync(
                new StaffAccountInput("new.acct", "New Accountant", AppRoles.Accountant, null, null, null),
                new AuditActor(adminId, "Admin", AppRoles.Administrator));
            Assert.Equal(StaffAccountOperation.Success, created.Status);
            temporary = created.TemporaryPassword!;
        }

        var client = _factory.CreateCookieClient();
        var signIn = await Browser.SignInStaffAsync(client, "new.acct", temporary);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Contains("/account/change-password", Browser.LocationPath(signIn), StringComparison.OrdinalIgnoreCase);

        var blocked = await client.GetAsync("/execution");
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Contains("/account/change-password", Browser.LocationPath(blocked), StringComparison.OrdinalIgnoreCase);

        var changePage = await client.GetStringAsync("/account/change-password?required=true");
        const string personal = "Personal#Password9876";
        var change = await Browser.PostFormAsync(client, "/account/change-password",
            [
                new("Required", "true"),
                new("Input.CurrentPassword", temporary),
                new("Input.NewPassword", personal),
                new("Input.ConfirmPassword", personal)
            ], Browser.ExtractToken(changePage));
        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/execution")).StatusCode);

        // The temporary password is single-use: it no longer signs in.
        var reuse = await Browser.SignInStaffAsync(_factory.CreateCookieClient(), "new.acct", temporary);
        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        var fresh = await Browser.SignInStaffAsync(_factory.CreateCookieClient(), "new.acct", personal);
        Assert.Equal(HttpStatusCode.Redirect, fresh.StatusCode);

        await using var context = TestDatabase.CreateContext();
        var actions = await context.AuditEntries.Select(entry => entry.Action).ToListAsync();
        Assert.Contains(AuditAction.StaffAccountCreated, actions);
        Assert.Contains(AuditAction.StaffPasswordSetupCompleted, actions);
        Assert.DoesNotContain(await context.AuditEntries.Select(entry => entry.Details ?? string.Empty).ToListAsync(),
            details => details.Contains(temporary, StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task DeactivatedStaff_LoseTheirSessionImmediately()
    {
        var adminId = await _factory.CreateStaffAsync("admin", AppRoles.Administrator, AdminPassword);
        var accountantId = await _factory.CreateStaffAsync("acct", AppRoles.Accountant, AccountantPassword);
        var accountant = _factory.CreateCookieClient();
        await Browser.SignInStaffAsync(accountant, "acct", AccountantPassword);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/execution")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var staff = scope.ServiceProvider.GetRequiredService<IStaffAccountService>();
            var result = await staff.DeactivateAsync(accountantId, new AuditActor(adminId, "Admin", AppRoles.Administrator));
            Assert.Equal(StaffAccountOperation.Success, result.Status);
        }

        var afterDeactivation = await accountant.GetAsync("/execution");
        Assert.Equal(HttpStatusCode.Redirect, afterDeactivation.StatusCode);
        Assert.Contains("/account/login", afterDeactivation.Headers.Location!.ToString());
    }

    [PostgresFact]
    public async Task LanguageSwitch_ChangesDirectionAndKeepsWesternDigits()
    {
        var client = _factory.CreateCookieClient();
        var persianDefault = await client.GetStringAsync("/");
        Assert.Contains("lang=\"fa\" dir=\"rtl\"", persianDefault);
        // Static assets are served under fingerprinted names, so only the stem is asserted.
        Assert.Contains("bootstrap.rtl.min", persianDefault);

        var token = Browser.ExtractToken(persianDefault);
        var switched = await Browser.PostFormAsync(client, "/language", [new("code", "en"), new("returnUrl", "/")], token);
        Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
        var english = await client.GetStringAsync("/");
        Assert.Contains("lang=\"en\" dir=\"ltr\"", english);
        Assert.Contains("Enter your username and employee access key", english);
    }
}
