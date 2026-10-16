using Microsoft.AspNetCore.Identity;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Services;


//  the first administrator from configuration when the explicit
// <c>bootstrap-admin</c> command is used. for initializing the first administrator. the bootstrapper ensures that only one administrator is created and prevents overwriting an existing account.

public sealed class AdministratorBootstrapper(
    IConfiguration configuration,
    UserManager<ApplicationUser> userManager)
{
    //  one administrator and refuses to overwrite an existing account.
    public async Task RunAsync()
    {
        var username = configuration["BootstrapAdmin:Username"];
        var password = configuration["BootstrapAdmin:Password"];
        var fullName = configuration["BootstrapAdmin:FullName"];

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException(
                "BootstrapAdmin:Username, BootstrapAdmin:Password, and BootstrapAdmin:FullName are required.");
        }

        if (await userManager.FindByNameAsync(username) is not null)
        {
            throw new InvalidOperationException(
                $"User '{username}' already exists. Bootstrap never overwrites an existing account.");
        }

        var administrator = new ApplicationUser
        {
            UserName = username.Trim(),
            FullName = fullName.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var createResult = await userManager.CreateAsync(administrator, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Administrator creation failed: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
        }

        var roleResult = await userManager.AddToRoleAsync(administrator, AppRoles.Administrator);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(administrator);
            throw new InvalidOperationException(
                $"Administrator role assignment failed: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        }
    }
}
