// Provides RoleManager for creating the application's named roles.
using Microsoft.AspNetCore.Identity;
using PurchaseApproval.Security;

namespace PurchaseApproval.Services;

/// <summary>Ensures the application's named Identity roles exist.</summary>
public sealed class ApplicationRoleInitializer(RoleManager<IdentityRole<int>> roleManager)
{
    /// <summary>Creates missing roles without changing existing role assignments.</summary>
    public async Task EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in AppRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<int>(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not create role '{roleName}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}
