using Microsoft.EntityFrameworkCore; /// Provides Entity Framework Core functionality for database access.
using PurchaseApproval.Data; /// Provides access to the application's database context.
using Microsoft.AspNetCore.Identity; /// Provides ASP.NET Core Identity functionality for authentication and user management.
using PurchaseApproval.Models; /// Provides access to the application's data models.
using PurchaseApproval.Security; /// Provides security-related functionality, such as authentication schemes and cookie events.
using PurchaseApproval.Services; /// Provides access to the application's service layer.
using System.Threading.RateLimiting; /// Provides rate limiting functionality for controlling request rates.

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
builder.Services.AddScoped<EmployeeCookieEvents>(); /// addScoped registers the EmployeeCookieEvents service with a scoped lifetime, meaning one instance per request. the scope is tied to the lifetime of the HTTP request.
builder.Services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>(); /// Registers the password hasher service for employees with a scoped lifetime.
builder.Services.AddScoped<IEmployeeAccessKeyService, EmployeeAccessKeyService>(); /// Registers the employee access key service with a scoped lifetime.
// Structure: Resolve the profile reader used by the employee payment-request page.
builder.Services.AddScoped<IEmployeeProfileService, EmployeeProfileService>(); /// Registers the employee profile service 
builder.Services.AddScoped<IPaymentRequestSubmissionService, PaymentRequestSubmissionService>(); /// Registers the payment request submission service with a scoped lifetime.
builder.Services.AddSingleton(TimeProvider.System); /// Registers the system time provider as a singleton service.
builder.Services.AddScoped<ApplicationRoleInitializer>(); /// Registers the application role initializer 
builder.Services.AddScoped<AdministratorBootstrapper>(); /// Registers the administrator bootstrapper

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

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("employee-submission", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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

app.UseRateLimiter();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
