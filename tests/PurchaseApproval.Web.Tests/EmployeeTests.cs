using PurchaseApproval.Models;
using Xunit;

public class EmployeeTests
{
    [Fact]
    public void NewEmployee_HasActiveAccessByDefault()
    {
        // Arrange and Act
        var employee = new Employee();

        // Assert
        Assert.Equal(
            EmployeeAccessStatus.Active,
            employee.AccessStatus
        );
    }

    [Fact]
    public void NewEmployee_StartsWithFirstCredentialVersion()
    {
        var employee = new Employee();

        Assert.Equal(1, employee.CredentialVersion);
    }
}