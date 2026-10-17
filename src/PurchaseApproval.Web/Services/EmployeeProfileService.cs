// Builds the filtered, read-only employee profile query.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

/// <summary>Supplies current employee details to the payment-request form.</summary>
// An interface lets the page use a simple test substitute without opening a database.
public interface IEmployeeProfileService
{
    /// <summary>Returns the profile only if the employee is active and the session version still matches.</summary>
    Task<EmployeeProfile?> GetActiveProfileAsync(
        int employeeId,
        int credentialVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads the administrator-maintained profile from the employee database table.</summary>
// Constructor injection supplies the request's DbContext, avoiding manual connection setup.
public sealed class EmployeeProfileService(ApplicationDbContext dbContext) : IEmployeeProfileService
{
    /// <inheritdoc />
    public async Task<EmployeeProfile?> GetActiveProfileAsync(
        int employeeId,
        int credentialVersion,
        CancellationToken cancellationToken = default)
    {
        // Recheck access and version when loading the form, in case an administrator
        // changed access after cookie validation. A revoked session must not receive profile details.
        return await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Id == employeeId
                && employee.AccessStatus == EmployeeAccessStatus.Active
                && employee.CredentialVersion == credentialVersion)
            // Select requests only these columns, avoiding credential hashes and admin notes.
            .Select(employee => new EmployeeProfile(
                employee.Id,
                employee.FullName,
                employee.Username,
                employee.PhoneNumber,
                employee.Department,
                employee.Branch,
                employee.JobTitle))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

/// <summary>Carries only the employee information that the request form may display.</summary>
// A record with constructor properties groups read-only values without separate backing fields.
public sealed record EmployeeProfile(
    int Id,
    string FullName,
    string Username,
    string? PhoneNumber,
    string? Department,
    string? Branch,
    string? JobTitle);
