// Uses the same password hasher as the employee access-key service.
using Microsoft.AspNetCore.Identity;
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;
/// <summary>
/// Contains unit tests for the EmployeeAccessKeyService class.
/// </summary>
public sealed class EmployeeAccessKeyServiceTests
{
    /// <summary>Initializes a new instance of the EmployeeAccessKeyService for testing.</summary>
    private readonly EmployeeAccessKeyService _service = new(new PasswordHasher<Employee>());

    [Fact]
    public void InitializeCredential_StoresHashAndReturnsVerifiableKey()
    {
        var employee = new Employee { Id = 42 };

        var accessKey = _service.InitializeCredential(employee);

        Assert.StartsWith("pak_42_", accessKey);
        Assert.NotEqual(accessKey, employee.AccessKeyHash);
        Assert.True(_service.Verify(employee, accessKey));
        Assert.Equal(1, employee.CredentialVersion);
    }

    /// <summary>Tests that verification rejects an incorrect access key.</summary>
    [Fact] 
    public void Verify_RejectsIncorrectKey()
    {
        var employee = new Employee { Id = 42 };
        _service.InitializeCredential(employee);

        Assert.False(_service.Verify(employee, "pak_not-the-issued-key"));
    }

    /// <summary>Tests that rotating the credential invalidates the old key and increments the version.</summary>
    [Fact]
    public void RotateCredential_InvalidatesOldKeyAndIncrementsVersion()
    {
        var employee = new Employee { Id = 42 };
        var oldKey = _service.InitializeCredential(employee);

        var newKey = _service.RotateCredential(employee);

        Assert.Equal(2, employee.CredentialVersion);
        Assert.False(_service.Verify(employee, oldKey));
        Assert.True(_service.Verify(employee, newKey));
    }

    /// <summary>Tests that generated access keys are unique even for the same employee ID.</summary>
    [Fact]
    public void GeneratedKeys_AreUnique()
    {
        var firstKey = _service.InitializeCredential(new Employee { Id = 42 });
        var secondKey = _service.InitializeCredential(new Employee { Id = 42 });

        Assert.NotEqual(firstKey, secondKey);
    }

    /// <summary>Tests that TryGetEmployeeId correctly reads the employee ID from a valid access key.</summary>
    [Fact]
    public void TryGetEmployeeId_ReadsIdFromGeneratedKey()
    {
        var accessKey = _service.InitializeCredential(new Employee { Id = 42 });

        var parsed = _service.TryGetEmployeeId(accessKey, out var employeeId);

        Assert.True(parsed);
        Assert.Equal(42, employeeId);
    }

    /// <summary>Tests that TryGetEmployeeId rejects access keys with invalid format.</summary>
    [Theory] 
    [InlineData("")]
    [InlineData("not-an-access-key")]
    [InlineData("pak_missing-id")]
    [InlineData("pak_0_secret")]
    public void TryGetEmployeeId_RejectsInvalidFormat(string accessKey)
    {
        Assert.False(_service.TryGetEmployeeId(accessKey, out _));
    }
}
