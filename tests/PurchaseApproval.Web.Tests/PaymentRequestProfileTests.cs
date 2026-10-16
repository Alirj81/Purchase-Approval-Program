using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Security;
using PurchaseApproval.Services;
using PurchaseApproval.Web.Pages.EmployeeAccess.Requests;

namespace PurchaseApproval.Web.Tests;

/// <summary>Purpose: Checks requester identity selection and profile loading in the request form.</summary>
public sealed class PaymentRequestProfileTests
{
    [Fact]
    public async Task Get_UsesDatabaseProfileInsteadOfStaleCookieNameOrQueryId()
    {
        var profiles = new TestProfileService();
        var page = CreatePage(profiles);
        page.HttpContext.Request.QueryString = new QueryString("?employeeId=999");

        var result = await page.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(42, profiles.RequestedEmployeeId);
        Assert.Equal(3, profiles.RequestedVersion);
        Assert.NotNull(page.Requester);
        Assert.Equal("Current profile name", page.Requester.FullName);
        Assert.Equal("Operations", page.Requester.Department);
        Assert.False(page.DetailsChecked);
    }

    [Fact]
    public async Task InvalidPost_KeepsProfileVisibleAlongsideFieldErrors()
    {
        var page = CreatePage(new TestProfileService());
        // Technique: Direct handler tests supply ModelState errors because the HTTP validation
        // pipeline does not run here. This tests the error path without pretending to test middleware.
        page.ModelState.AddModelError("Input.PayeeName", "Enter the payee's name.");

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(page.Requester);
        Assert.False(page.DetailsChecked);
        Assert.False(page.ModelState.IsValid);
    }

    [Fact]
    public async Task ValidPost_LoadsCurrentProfileAndChecksDetails()
    {
        var page = CreatePage(new TestProfileService());
        page.Input.PayeeName = "Example supplier";
        page.Input.Justification = "Office supplies";

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(page.Requester);
        Assert.True(page.DetailsChecked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableProfile_EndsEmployeeSession(bool isPost)
    {
        // Security: A null profile represents a deleted employee, inactive access, or stale version.
        var profiles = new TestProfileService { Profile = null };
        var page = CreatePage(profiles);

        var result = isPost ? await page.OnPostAsync() : await page.OnGetAsync();

        var signOut = Assert.IsType<SignOutResult>(result);
        Assert.Equal(EmployeeAuthenticationDefaults.AuthenticationScheme,
            Assert.Single(signOut.AuthenticationSchemes));
        Assert.Equal("/", signOut.Properties?.RedirectUri);
        Assert.Null(page.Requester);
        Assert.False(page.DetailsChecked);
    }

    [Theory]
    [InlineData(null, "3")]
    [InlineData("not-an-id", "3")]
    [InlineData("0", "3")]
    [InlineData("42", null)]
    [InlineData("42", "0")]
    public async Task InvalidClaims_EndSessionWithoutLookingUpAProfile(string? id, string? version)
    {
        var profiles = new TestProfileService();
        var page = CreatePage(profiles, id, version);

        var result = await page.OnGetAsync();

        Assert.IsType<SignOutResult>(result);
        Assert.Equal(0, profiles.CallCount);
    }

    [Fact]
    public async Task StaffIdentityAlone_CannotSelectAnEmployeeWithTheSameId()
    {
        var profiles = new TestProfileService();
        var page = CreatePage(profiles, authenticationType: IdentityConstants.ApplicationScheme);

        var result = await page.OnGetAsync();

        Assert.IsType<SignOutResult>(result);
        Assert.Equal(0, profiles.CallCount);
    }

    private static CreateModel CreatePage(
        TestProfileService profiles,
        string? id = "42",
        string? version = "3",
        string authenticationType = EmployeeAuthenticationDefaults.AuthenticationScheme)
    {
        var claims = new List<Claim> { new(ClaimTypes.GivenName, "Old cookie name") };
        if (id is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        }

        if (version is not null)
        {
            claims.Add(new Claim(EmployeeAuthenticationDefaults.CredentialVersionClaim, version));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType))
        };
        return new CreateModel(profiles)
        {
            PageContext = new PageContext { HttpContext = context }
        };
    }

    // Technique: A small substitute records the requested identity and controls whether a profile
    // exists. It isolates page behavior; database filtering and real cookie handling need integration tests.
    private sealed class TestProfileService : IEmployeeProfileService
    {
        public EmployeeProfile? Profile { get; init; } = new(
            42, "Current profile name", "employee42", null, "Operations", null, "Coordinator");
        public int CallCount { get; private set; }
        public int RequestedEmployeeId { get; private set; }
        public int RequestedVersion { get; private set; }

        public Task<EmployeeProfile?> GetActiveProfileAsync(
            int employeeId, int credentialVersion, CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedEmployeeId = employeeId;
            RequestedVersion = credentialVersion;
            return Task.FromResult(Profile);
        }
    }
}
