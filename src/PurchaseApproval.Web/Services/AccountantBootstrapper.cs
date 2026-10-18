using Microsoft.AspNetCore.Identity;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Services;

/// <summary>Creates one accountant from configuration only when explicitly requested.</summary>
public sealed class AccountantBootstrapper(
    IConfiguration configuration,
    UserManager<ApplicationUser> userManager)
{
    public async Task RunAsync()
    {
        var username = configuration["BootstrapAccountant:Username"];
        var password = configuration["BootstrapAccountant:Password"];
        var fullName = configuration["BootstrapAccountant:FullName"];

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException(
                "BootstrapAccountant:Username, BootstrapAccountant:Password, and BootstrapAccountant:FullName are required.");
        }

        if (await userManager.FindByNameAsync(username) is not null)
        {
            throw new InvalidOperationException(
                $"User '{username}' already exists. Bootstrap never overwrites an existing account.");
        }

        var accountant = new ApplicationUser
        {
            UserName = username.Trim(),
            FullName = fullName.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var createResult = await userManager.CreateAsync(accountant, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Accountant creation failed: {string.Join(", ", createResult.Errors.Select(error => error.Description))}");
        }

        var roleResult = await userManager.AddToRoleAsync(accountant, AppRoles.Accountant);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(accountant);
            throw new InvalidOperationException(
                $"Accountant role assignment failed: {string.Join(", ", roleResult.Errors.Select(error => error.Description))}");
        }
    }
}
