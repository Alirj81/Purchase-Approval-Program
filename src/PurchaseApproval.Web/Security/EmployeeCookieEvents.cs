using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Security;

/// <summary>
///  Revalidates every employee cookie against the current database record. A disabled,
/// revoked, deleted, or rotated credential therefore invalidates existing sessions.
/// </summary>
public sealed class EmployeeCookieEvents(ApplicationDbContext dbContext)
    : CookieAuthenticationEvents
{
    /// <inheritdoc />
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // Syntax: ?. handles missing claims without a null-reference error; TryParse below rejects them.
        var employeeIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var credentialVersionValue = context.Principal?.FindFirstValue(
            EmployeeAuthenticationDefaults.CredentialVersionClaim);

        if (!int.TryParse(employeeIdValue, out var employeeId)
            || !int.TryParse(credentialVersionValue, out var credentialVersion))
        {
            await RejectAsync(context);
            return;
        }

        // Technique: AsNoTracking avoids change-tracking overhead for this read-only lookup.
        // RequestAborted cancels the database query if the browser disconnects.
        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == employeeId, context.HttpContext.RequestAborted);

        // Security: Compare the cookie's version with today's database value. A signed cookie
        // may still be outdated after an administrator disables access or generates a new key.
        if (employee is null
            || employee.AccessStatus != EmployeeAccessStatus.Active
            || employee.CredentialVersion != credentialVersion)
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            EmployeeAuthenticationDefaults.AuthenticationScheme);
    }
}
