using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using Microsoft.AspNetCore.Identity;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

// Purpose: Connect configuration, database access, authentication, and Razor Pages at application startup.
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException( // Ensure the connection string is  provided
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString)); // Configure Entity Framework to use PostgreSQL

builder.Services // Configure Identity for the application
    .AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
    {
        options.User.RequireUniqueEmail = false;

        options.Password.RequiredLength = 12;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 6;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
    options.Cookie.Name = "PurchaseApproval.PrivilegedSession";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services
    .AddAuthentication()
    .AddCookie(
        EmployeeAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.LoginPath = "/";
            options.AccessDeniedPath = "/";
            options.Cookie.Name = "PurchaseApproval.EmployeeSession";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.EventsType = typeof(EmployeeCookieEvents);
        });

// Structure: AddScoped supplies one service instance per request and shares that request's DbContext.
// This lets page constructors declare their dependencies instead of creating database services themselves.
builder.Services.AddScoped<EmployeeCookieEvents>();
builder.Services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>();
builder.Services.AddScoped<IEmployeeAccessKeyService, EmployeeAccessKeyService>();
// Structure: Resolve the profile reader used by the employee payment-request page.
builder.Services.AddScoped<IEmployeeProfileService, EmployeeProfileService>();
builder.Services.AddScoped<ApplicationRoleInitializer>();
builder.Services.AddScoped<AdministratorBootstrapper>();

builder.Services.AddAuthorization(options => // Configure authorization policies for the application
{
    options.AddPolicy(
        AppPolicies.CanManageEmployees,
        policy => policy.RequireRole(AppRoles.Administrator));

    options.AddPolicy(
        AppPolicies.CanDecideRequests,
        policy => policy.RequireRole(
            AppRoles.Administrator));

    options.AddPolicy(
        AppPolicies.CanExecutePayments,
        policy => policy.RequireRole(
            AppRoles.Administrator,
            AppRoles.Accountant));
});

var app = builder.Build();

// Role initialization is idempotent and gives policy names a real database backing.
await using (var startupScope = app.Services.CreateAsyncScope())
{
    var roleInitializer = startupScope.ServiceProvider
        .GetRequiredService<ApplicationRoleInitializer>();
    await roleInitializer.EnsureRolesAsync();

    if (args.Contains("bootstrap-admin", StringComparer.OrdinalIgnoreCase))
    {
        var bootstrapper = startupScope.ServiceProvider
            .GetRequiredService<AdministratorBootstrapper>();
        await bootstrapper.RunAsync();
        return;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();
// Security: Identify the caller before authorization decides which pages they may access.
app.UseAuthentication();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
