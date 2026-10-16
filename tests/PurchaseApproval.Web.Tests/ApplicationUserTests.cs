using PurchaseApproval.Models;
using Xunit;

namespace PurchaseApproval.Web.Tests;

public class ApplicationUserTests
{
    /// <summary>Initializes a new instance of the ApplicationUserTests class for testing.</summary>
    [Fact]
    public void NewApplicationUser_IsActiveByDefault()
    {
        var applicationUser = new ApplicationUser();
        Assert.True(applicationUser.IsActive);
    }
}