using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Models;

namespace PurchaseApproval.Data;

public class ApplicationDbContext
    : IdentityDbContext<Administrator, IdentityRole<int>, int>
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

        ConfigureAdministrators(modelBuilder);
        ConfigureEmployees(modelBuilder);
    }

    private static void ConfigureAdministrators(ModelBuilder modelBuilder) // Configure the Administrator entity 
    {
        var administrator =
            modelBuilder.Entity<Administrator>();

        administrator.ToTable("Administrators");

        administrator
            .Property(admin => admin.FullName)
            .HasMaxLength(200)
            .IsRequired();

        administrator
            .Property(admin => admin.IsActive)
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