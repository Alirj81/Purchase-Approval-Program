using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Models;

namespace PurchaseApproval.Data;

public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) // Configure the model (adds Identity’s tables and relationships)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureApplicationUsers(modelBuilder);
        ConfigureEmployees(modelBuilder);
    }

    private static void ConfigureApplicationUsers(
    ModelBuilder modelBuilder)
{
    var user = modelBuilder.Entity<ApplicationUser>();

    user.ToTable("ApplicationUsers");

    user
        .Property(item => item.FullName)
        .HasMaxLength(200)
        .IsRequired();

    user
        .Property(item => item.Department)
        .HasMaxLength(100);

    user
        .Property(item => item.Branch)
        .HasMaxLength(100);

    user
        .Property(item => item.JobTitle)
        .HasMaxLength(100);

    user
        .Property(item => item.IsActive)
        .HasDefaultValue(true);
}

    private static void ConfigureEmployees(ModelBuilder modelBuilder) // Configure the Employee entity
    {
        var employee = modelBuilder.Entity<Employee>();

        employee.ToTable("Employees");

        employee
            .Property(item => item.FullName)
            .HasMaxLength(200)
            .IsRequired();

        employee
            .Property(item => item.Username)
            .HasMaxLength(100)
            .IsRequired();

        employee
            .Property(item => item.NormalizedUsername)
            .HasMaxLength(100)
            .IsRequired();

        employee
            .HasIndex(item => item.NormalizedUsername)
            .IsUnique();

        employee
            .Property(item => item.PhoneNumber)
            .HasMaxLength(50);

        employee
            .Property(item => item.Department)
            .HasMaxLength(100);

        employee
            .Property(item => item.Branch)
            .HasMaxLength(100);

        employee
            .Property(item => item.JobTitle)
            .HasMaxLength(100);

        employee
            .Property(item => item.AdminNotes)
            .HasMaxLength(1000);

        employee
            .Property(item => item.AccessKeyHash)
            .HasMaxLength(500)
            .IsRequired();

        employee
            .Property(item => item.CredentialVersion)
            .HasDefaultValue(1);

        employee
            .Property(item => item.AccessStatus)
            .HasConversion<int>()
            .HasDefaultValue(EmployeeAccessStatus.Active);

        employee
            .HasIndex(item => item.AccessStatus);
    }
}