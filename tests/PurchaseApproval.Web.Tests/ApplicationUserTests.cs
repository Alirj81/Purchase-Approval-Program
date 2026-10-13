using PurchaseApproval.Models;
using Xunit;

namespace PurchaseApproval.Web.Tests;

public class ApplicationUserTests
{
    [Fact]
    public void NewApplicationUser_IsActiveByDefault()
    {
        var applicationUser = new ApplicationUser();

        Assert.True(applicationUser.IsActive);
    }
}