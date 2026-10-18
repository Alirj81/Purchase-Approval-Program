using System.ComponentModel.DataAnnotations;
// Restricts employee creation to administrators.
using Microsoft.AspNetCore.Authorization;
// Creates the Identity relationship used by the page when needed.
using Microsoft.AspNetCore.Identity;
// Provides binding and redirect results for the form.
using Microsoft.AspNetCore.Mvc;
// Provides the Razor Page base class.
using Microsoft.AspNetCore.Mvc.RazorPages;
// Checks usernames before saving a new employee record.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages.Admin.Employees;

/// <summary>Creates an employee profile and issues its first one-time access key.</summary>
[Authorize(Policy = AppPolicies.CanManageEmployees)]
public sealed class CreateModel(
    ApplicationDbContext dbContext,
    ILookupNormalizer lookupNormalizer,
    IEmployeeAccessKeyService accessKeyService) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? IssuedAccessKey { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var username = Input.Username.Trim();
        var normalizedUsername = lookupNormalizer.NormalizeName(username);
        if (await dbContext.Employees.AnyAsync(
            item => item.NormalizedUsername == normalizedUsername))
        {
            ModelState.AddModelError("Input.Username", "This username is already in use.");
            return Page();
        }

        var employee = new Employee
        {
            FullName = Input.FullName.Trim(),
            Username = username,
            NormalizedUsername = normalizedUsername,
            PhoneNumber = Clean(Input.PhoneNumber),
            Department = Clean(Input.Department),
            Branch = Clean(Input.Branch),
            JobTitle = Clean(Input.JobTitle),
            AccessKeyHash = "pending",
            AccessStatus = EmployeeAccessStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        IssuedAccessKey = accessKeyService.InitializeCredential(employee);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Page();
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class InputModel
    {
        [Required, StringLength(200)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 3)]
        [RegularExpression("^[A-Za-z0-9._-]+$", ErrorMessage = "Use letters, numbers, periods, hyphens, or underscores only.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [StringLength(50)]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [StringLength(100)]
        [Display(Name = "Department")]
        public string? Department { get; set; }

        [StringLength(100)]
        [Display(Name = "Branch")]
        public string? Branch { get; set; }

        [StringLength(100)]
        [Display(Name = "Job title")]
        public string? JobTitle { get; set; }
    }
}
