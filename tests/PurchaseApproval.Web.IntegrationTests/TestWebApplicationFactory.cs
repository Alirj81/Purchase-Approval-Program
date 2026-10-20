// Hosts the real application in-process against the test database for HTTP-level tests.
// Settings passed to the constructor override appsettings values (for example lower limits so a
// throttling test does not need dozens of requests). The environment name "Testing" keeps the
// production-only guards (HTTPS-only cookies, mandatory Data Protection folder) switched off.
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.IntegrationTests;

public sealed class TestWebApplicationFactory(
    IReadOnlyDictionary<string, string>? settings = null,
    string environment = "Testing")
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Production" is used only by start-up guard tests; nothing is served in that mode.
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            TestDatabase.ConnectionString ?? throw new InvalidOperationException(TestDatabase.SkipReason));
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }

    public HttpClient CreateCookieClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    /// <summary>Creates an active staff account with a personal (non-temporary) password.</summary>
    public async Task<int> CreateStaffAsync(string username, string role, string password, string fullName = "Staff Member")
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = username, FullName = fullName, IsActive = true };
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Description)));
        }

        await userManager.AddToRoleAsync(user, role);
        return user.Id;
    }

    public async Task<(int Id, string Username, string AccessKey)> CreateEmployeeAsync(string username, string fullName = "Test Employee")
    {
        using var scope = Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEmployeeAccessService>();
        var result = await service.CreateAsync(
            username,
            new EmployeeProfileInput(fullName, null, "Operations", null, null, null),
            new AuditActor(null, "Test seed", "System"));
        if (result.Status != EmployeeAccessOperation.Success)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors));
        }

        return (result.EmployeeId!.Value, username, result.IssuedAccessKey!);
    }
}

/// <summary>Helpers for form-based pages: anti-forgery tokens, hidden fields and sign-in flows.</summary>
public static class Browser
{
    private static readonly Regex TokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static string ExtractToken(string html)
    {
        var match = TokenPattern.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("No anti-forgery token found in the page.");
        }

        return match.Groups[1].Value;
    }

    public static string ExtractHidden(string html, string name)
    {
        var match = Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        if (!match.Success)
        {
            match = Regex.Match(html, $"value=\"([^\"]*)\"[^>]*name=\"{Regex.Escape(name)}\"");
        }

        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException($"Hidden field {name} not found.");
    }

    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> fields, string token)
    {
        var content = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token)));
        return await client.PostAsync(url, content);
    }

    /// <summary>Switches the client's UI language to English so tests can assert on English text.</summary>
    public static async Task UseEnglishAsync(HttpClient client)
    {
        var page = await client.GetStringAsync("/");
        var response = await PostFormAsync(client, "/language",
            [new("code", "en"), new("returnUrl", "/")], ExtractToken(page));
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException("Language switch failed.");
        }
    }

    /// <summary>Text of the validation summary (alert-danger) on a page, for assertion messages.</summary>
    public static async Task<string> ValidationErrorsAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var matches = Regex.Matches(html, "<div class=\"alert alert-danger[^\"]*\"[^>]*>(.*?)</div>", RegexOptions.Singleline);
        var text = string.Join(" | ", matches.Select(match => Regex.Replace(match.Groups[1].Value, "<[^>]+>", " ")));
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    public static string LocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location ?? throw new InvalidOperationException("No Location header.");
        return location.IsAbsoluteUri ? location.PathAndQuery : location.ToString();
    }

    public static async Task<HttpResponseMessage> SignInStaffAsync(HttpClient client, string username, string password)
    {
        var page = await client.GetStringAsync("/account/login");
        return await PostFormAsync(client, "/account/login",
            [new("Input.Username", username), new("Input.Password", password)], ExtractToken(page));
    }

    public static async Task<HttpResponseMessage> SignInEmployeeAsync(HttpClient client, string username, string accessKey)
    {
        var page = await client.GetStringAsync("/");
        return await PostFormAsync(client, "/",
            [new("Input.Username", username), new("Input.AccessKey", accessKey)], ExtractToken(page));
    }

    /// <summary>Submits a one-row request through the employee or administrator form and returns the redirect target.</summary>
    public static async Task<string> SubmitRequestAsync(
        HttpClient client, string createUrl, string payee, long amount, byte[]? pdfAttachment = null)
    {
        var page = await client.GetStringAsync(createUrl);
        var token = ExtractToken(page);
        var submissionKey = ExtractHidden(page, "SubmissionKey");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(submissionKey), "SubmissionKey" },
            { new StringContent(payee), "Input.PayeeName" },
            { new StringContent("Integration test purpose"), "Input.Justification" },
            { new StringContent("Printer paper"), "Input.Lines[0].Description" },
            { new StringContent(amount.ToString()), "Input.Lines[0].AmountRials" },
            { new StringContent("true"), "Input.Lines[0].HasInvoice" }
        };
        if (pdfAttachment is not null)
        {
            var file = new ByteArrayContent(pdfAttachment);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(file, "Attachments", "evidence.pdf");
        }

        var response = await client.PostAsync(createUrl, form);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException(
                $"Submission did not redirect ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        return response.Headers.Location!.ToString();
    }
}
