using PurchaseApproval.Models;
using Xunit;

namespace PurchaseApproval.Web.Tests;

/// <summary>
/// Contains unit tests for the ApplicationUser model.
/// </summary>
public class ApplicationUserTests
{
    /// <summary>
    /// Verifies that a newly created ApplicationUser is active by default.
    /// </summary>
    [Fact]
    public void NewApplicationUser_IsActiveByDefault()
    {
        var applicationUser = new ApplicationUser();

        Assert.True(applicationUser.IsActive);
    }
}