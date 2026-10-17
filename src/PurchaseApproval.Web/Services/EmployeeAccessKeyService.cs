using System.Security.Cryptography;
// PasswordHasher protects employee access keys before they reach the database.
using Microsoft.AspNetCore.Identity;
// Base64Url creates compact keys that are safe to copy through a browser form.
using Microsoft.AspNetCore.WebUtilities;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

/// <summary>Defines access-key generation, lookup, and verification for employee sign-in.</summary>
public interface IEmployeeAccessKeyService
{
    /// <summary>Creates the first credential for a new employee.</summary>
    string InitializeCredential(Employee employee);

    /// <summary>Replaces an employee credential and invalidates older sessions.</summary>
    string RotateCredential(Employee employee);

    /// <summary>Verifies a presented key without exposing the stored password hash.</summary>
    bool Verify(Employee employee, string accessKey);

    /// <summary>Reads the employee ID embedded in a valid access-key format.</summary>
    bool TryGetEmployeeId(string accessKey, out int employeeId);
}

/// <inheritdoc />
public sealed class EmployeeAccessKeyService(IPasswordHasher<Employee> passwordHasher)
    : IEmployeeAccessKeyService
{
    // 32 cryptographically random bytes provide 256 bits of secret material per key.
    private const int RandomByteCount = 32;
    private const string KeyPrefix = "pak_";
    private const char KeySeparator = '_';

    /// <inheritdoc />
    public string InitializeCredential(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);
        employee.CredentialVersion = 1;
        return SetCredential(employee);
    }

    /// <inheritdoc />
    public string RotateCredential(Employee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);
    // checked throws on overflow instead of silently wrapping the version number.
        employee.CredentialVersion = checked(employee.CredentialVersion + 1);
        return SetCredential(employee);
    }

    /// <inheritdoc />
    public bool Verify(Employee employee, string accessKey)
    {
        ArgumentNullException.ThrowIfNull(employee);

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(employee.AccessKeyHash))
        {
            return false;
        }

        var result = passwordHasher.VerifyHashedPassword(
            employee,
            employee.AccessKeyHash,
            accessKey);

        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }

    /// <inheritdoc />
    // Parsing locates a record; it does not authenticate. Verify must check the full key.
    public bool TryGetEmployeeId(string accessKey, out int employeeId)
    {
        employeeId = 0;

        if (string.IsNullOrWhiteSpace(accessKey)
            || !accessKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var idStart = KeyPrefix.Length;
        var separatorIndex = accessKey.IndexOf(KeySeparator, idStart);

        return separatorIndex > idStart
            && separatorIndex < accessKey.Length - 1
            && int.TryParse(accessKey.AsSpan(idStart, separatorIndex - idStart), out employeeId)
            && employeeId > 0;
    }

    private string SetCredential(Employee employee)
    {
        if (employee.Id <= 0)
        {
            throw new InvalidOperationException(
                "The employee must be saved before an access key can be generated.");
        }

    // Base64Url turns random bytes into copyable text without changing their randomness.
        var randomPart = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(RandomByteCount));
        var accessKey = $"{KeyPrefix}{employee.Id}{KeySeparator}{randomPart}";

    // PasswordHasher stores a salted one-way hash; the database cannot recover the key.
        employee.AccessKeyHash = passwordHasher.HashPassword(employee, accessKey);
        return accessKey;
    }
}
