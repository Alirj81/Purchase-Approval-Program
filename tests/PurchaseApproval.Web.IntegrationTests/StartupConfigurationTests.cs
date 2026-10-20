// Start-up configuration guards: invalid limits, a missing key folder in Production and an
// inaccessible key folder must stop the host with a message that names the setting to fix.
// The last test shows why the folder matters: a staff session signed in by one host is still
// valid for a second host that shares the key folder (an application-pool recycle), and is
// rejected by a host whose keys live elsewhere. The hosts here are Kestrel/in-process, so the
// IIS-specific parts (pool identity, DPAPI under that identity) remain a server rehearsal item.
using System.Net;
using PurchaseApproval.Security;

namespace PurchaseApproval.Web.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class StartupConfigurationTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (TestDatabase.IsAvailable)
        {
            await TestDatabase.ResetAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PostgresFact]
    public void InvalidLimits_StopTheHostAndNameEverySettingToFix()
    {
        using var factory = new TestWebApplicationFactory(new Dictionary<string, string>
        {
            ["Limits:DailySubmissionQuota"] = "0",
            ["Limits:MaximumAttachmentBytes"] = "99999999",
            ["Limits:LoginThrottling:FailuresPerIp"] = "-1"
        });

        var exception = Assert.ThrowsAny<Exception>(() => _ = factory.Services);

        Assert.Contains("Limits:DailySubmissionQuota", exception.Message);
        Assert.Contains("Limits:MaximumAttachmentBytes", exception.Message);
        Assert.Contains("Limits:LoginThrottling:FailuresPerIp", exception.Message);
    }

    [PostgresFact]
    public void Production_WithoutKeyDirectory_RefusesToStart()
    {
        using var factory = new TestWebApplicationFactory(environment: "Production");

        var exception = Assert.ThrowsAny<Exception>(() => _ = factory.Services);

        Assert.Contains("DataProtection:KeyDirectory", exception.Message);
    }

    [PostgresFact]
    public void MissingKeyDirectory_FailsAtStartupWithThePathAndIdentity()
    {
        var missing = Path.Combine(Path.GetTempPath(), "purchase-approval-missing-keys-" + Guid.NewGuid().ToString("N"));
        using var factory = new TestWebApplicationFactory(new Dictionary<string, string>
        {
            ["DataProtection:KeyDirectory"] = missing
        });

        var exception = Assert.ThrowsAny<Exception>(() => _ = factory.Services);

        Assert.Contains("DataProtection:KeyDirectory", exception.Message);
        Assert.Contains(missing, exception.Message);
        Assert.Contains(DataProtectionKeyDirectory.CurrentIdentity(), exception.Message);
    }

    [PostgresFact]
    public async Task StaffSession_SurvivesARestartThatSharesTheKeyFolder_AndNotOneThatDoesNot()
    {
        var sharedKeys = Directory.CreateTempSubdirectory("purchase-approval-keys-").FullName;
        var otherKeys = Directory.CreateTempSubdirectory("purchase-approval-other-keys-").FullName;
        try
        {
            string cookie;
            using (var beforeRecycle = new TestWebApplicationFactory(KeySettings(sharedKeys)))
            {
                await beforeRecycle.CreateStaffAsync("recycle.admin", AppRoles.Administrator, "Recycle#Password123");
                var client = beforeRecycle.CreateCookieClient();
                var signIn = await Browser.SignInStaffAsync(client, "recycle.admin", "Recycle#Password123");
                Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
                cookie = SessionCookie(signIn);
                Assert.NotEmpty(Directory.GetFiles(sharedKeys, "key-*.xml"));
            }

            // Same key folder after the "recycle": the cookie is still readable, no new sign-in needed.
            using (var afterRecycle = new TestWebApplicationFactory(KeySettings(sharedKeys)))
            {
                var response = await RequestWithCookie(afterRecycle, cookie);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            // Different key folder (a rebuilt server without the keys): the cookie cannot be read.
            using (var elsewhere = new TestWebApplicationFactory(KeySettings(otherKeys)))
            {
                var response = await RequestWithCookie(elsewhere, cookie);
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.StartsWith("/account/login", Browser.LocationPath(response), StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(sharedKeys, recursive: true);
            Directory.Delete(otherKeys, recursive: true);
        }
    }

    private static Dictionary<string, string> KeySettings(string directory) => new()
    {
        ["DataProtection:KeyDirectory"] = directory
    };

    private static string SessionCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies), "No Set-Cookie header on sign-in.");
        var session = cookies.Single(value => value.StartsWith("PurchaseApproval.PrivilegedSession=", StringComparison.Ordinal));
        return session.Split(';')[0];
    }

    private static async Task<HttpResponseMessage> RequestWithCookie(TestWebApplicationFactory factory, string cookie)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/requests");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }
}
