using PurchaseApproval.Models;
using Xunit;

namespace PurchaseApproval.Web.Tests;

public class AdministratorTests
{
    [Fact]
    public void NewAdministrator_IsActiveByDefault()
    {
        var administrator = new Administrator();

        Assert.True(administrator.IsActive);
    }
}