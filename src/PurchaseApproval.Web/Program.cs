// Application start-up: configuration, PostgreSQL/EF Core, Identity for staff, the separate
// employee access-key cookie, authorization policies, localization, rate limiting, security
// headers and the hosted closure worker. Each block below says why it exists; the operational
// values it reads are documented in docs/DEPLOYMENT-WINDOWS-IIS.md.
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;
using PurchaseApproval.Web;
using PurchaseApproval.Web.Localization;
// The submission pages use this to limit repeated attempts from one account.
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Configuration objects
// ---------------------------------------------------------------------------------------------
builder.Services.Configure<ApplicationLimitsOptions>(
    builder.Configuration.GetSection(ApplicationLimitsOptions.SectionName));
// A wrong limit (quota 0, attachment ceiling above the database constraint) stops the host at
// start-up with every offending key named, instead of silently misbehaving in production.
builder.Services.AddSingleton<IValidateOptions<ApplicationLimitsOptions>, ApplicationLimitsValidator>();
builder.Services.AddOptions<ApplicationLimitsOptions>().ValidateOnStart();
var limits = builder.Configuration
    .GetSection(ApplicationLimitsOptions.SectionName)
    .Get<ApplicationLimitsOptions>() ?? new ApplicationLimitsOptions();

// ---------------------------------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------------------------------
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException( // Ensure the connection string is provided
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

// ---------------------------------------------------------------------------------------------
// Identity (administrators and accountants)
// ---------------------------------------------------------------------------------------------
builder.Services
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

// Validate the security stamp on every request so a deactivation, password reset or role change
// ends existing sessions immediately instead of after the default 30-minute interval.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);

var isProduction = builder.Environment.IsProduction();
// Production serves HTTPS only, so cookies must never travel over plain HTTP there.
var cookieSecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
    options.Cookie.Name = "PurchaseApproval.PrivilegedSession";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context =>
    {
        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = context.Principal is null
            ? null
            : await userManager.GetUserAsync(context.Principal);
        if (user is null || !user.IsActive)
        {
            context.RejectPrincipal();
            return;
        }

        // Lets ForcedPasswordChangeFilter confine temporary-password holders without a second query.
        context.HttpContext.Items[ForcedPasswordChangeFilter.ItemsKey] = user.MustChangePassword;
        await SecurityStampValidator.ValidatePrincipalAsync(context);
    };
});

// ---------------------------------------------------------------------------------------------
// Employee access-key cookie (separate scheme, separate cookie)
// ---------------------------------------------------------------------------------------------
builder.Services
    .AddAuthentication()
    .AddCookie(
        EmployeeAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.LoginPath = "/";
            options.AccessDeniedPath = "/";
            options.Cookie.Name = "PurchaseApproval.EmployeeSession";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.EventsType = typeof(EmployeeCookieEvents);
        });

// ---------------------------------------------------------------------------------------------
// Application services. AddScoped supplies one instance per request sharing that request's
// DbContext; singletons hold no per-request state.
// ---------------------------------------------------------------------------------------------
builder.Services.AddMemoryCache();
builder.Services.AddScoped<EmployeeCookieEvents>();
builder.Services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>();
builder.Services.AddScoped<IEmployeeAccessKeyService, EmployeeAccessKeyService>();
builder.Services.AddScoped<IEmployeeAccessService, EmployeeAccessService>();
builder.Services.AddScoped<IEmployeeProfileService, EmployeeProfileService>();
builder.Services.AddScoped<ISubmissionQuotaService, SubmissionQuotaService>();
builder.Services.AddScoped<IPaymentRequestSubmissionService, PaymentRequestSubmissionService>();
builder.Services.AddScoped<IApprovalDecisionService, ApprovalDecisionService>();
builder.Services.AddScoped<ITransferExecutionService, TransferExecutionService>();
builder.Services.AddScoped<ITransferClosureProcessor, TransferClosureProcessor>();
builder.Services.AddHostedService<TransferClosureHostedService>();
builder.Services.AddScoped<IAdministratorHistoryService, AdministratorHistoryService>();
builder.Services.AddScoped<IStaffAccountService, StaffAccountService>();
builder.Services.AddSingleton<ILoginThrottlingService, LoginThrottlingService>();
// Reporting reads retained snapshots and creates XLSX files without changing workflow state.
builder.Services.AddScoped<IExcelReportService, ExcelReportService>();
builder.Services.AddSingleton<IExcelWorkbookBuilder, ExcelWorkbookBuilder>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ApplicationRoleInitializer>();
builder.Services.AddScoped<AdministratorBootstrapper>();
builder.Services.AddScoped<AccountantBootstrapper>();
// `environment-check` command for operators (docs/DEPLOYMENT-WINDOWS-IIS.md).
builder.Services.AddScoped<EnvironmentCheck>();

// ---------------------------------------------------------------------------------------------
// Localization: Persian UI by default with an English switch; formatting stays invariant.
// ---------------------------------------------------------------------------------------------
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services
    .AddRazorPages(options => options.Conventions.ConfigureFilter(new ForcedPasswordChangeFilter()))
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

var requestLocalization = new RequestLocalizationOptions()
    .SetDefaultCulture(LanguageCookieRequestCultureProvider.FormattingCulture)
    .AddSupportedCultures(LanguageCookieRequestCultureProvider.FormattingCulture)
    .AddSupportedUICultures(LanguageCookieRequestCultureProvider.SupportedUiCultures);
requestLocalization.DefaultRequestCulture = new RequestCulture(
    LanguageCookieRequestCultureProvider.FormattingCulture,
    LanguageCookieRequestCultureProvider.DefaultUiCulture);
requestLocalization.RequestCultureProviders.Clear();
requestLocalization.RequestCultureProviders.Add(new LanguageCookieRequestCultureProvider());

// ---------------------------------------------------------------------------------------------
// Authorization policies
// ---------------------------------------------------------------------------------------------
builder.Services.AddAuthorization(options =>
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

// ---------------------------------------------------------------------------------------------
// Short-window burst guard for request submission (both requester types). The daily quota is a
// separate database rule in SubmissionQuotaService; this limiter only slows rapid re-posting.
// ---------------------------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("submission-burst", httpContext =>
    {
        // Key by authentication kind + account ID so an employee and an administrator with the
        // same numeric ID never share a bucket; fall back to the client address when anonymous.
        var identity = httpContext.User.Identities.FirstOrDefault(item => item.IsAuthenticated);
        var accountId = identity?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var key = accountId is null
            ? $"anonymous:{httpContext.Connection.RemoteIpAddress}"
            : $"{identity!.AuthenticationType}:{accountId}";
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, limits.SubmissionBurstLimit),
                Window = TimeSpan.FromMinutes(Math.Max(1, limits.SubmissionBurstWindowMinutes)),
                QueueLimit = 0
            });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ---------------------------------------------------------------------------------------------
// Upload size: at most MaximumAttachmentCount files of MaximumAttachmentBytes plus form overhead.
// ---------------------------------------------------------------------------------------------
var maximumRequestBytes = limits.MaximumAttachmentCount * limits.MaximumAttachmentBytes + 1024 * 1024;
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = maximumRequestBytes);
builder.Services.Configure<IISServerOptions>(options =>
    options.MaxRequestBodySize = maximumRequestBytes);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maximumRequestBytes);

// ---------------------------------------------------------------------------------------------
// Data Protection keys sign cookies and anti-forgery tokens. Without a persistent store every
// restart or IIS recycle would invalidate all sessions, so production must configure a folder.
// ---------------------------------------------------------------------------------------------
var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(keyDirectory))
{
    // Fail now, naming the folder and the account, rather than with an HTTP 500 at the first sign-in.
    var keyDirectoryProblem = DataProtectionKeyDirectory.Probe(keyDirectory);
    if (keyDirectoryProblem is not null)
    {
        throw new InvalidOperationException(keyDirectoryProblem);
    }

    var dataProtection = builder.Services
        .AddDataProtection()
        .SetApplicationName("PurchaseApproval")
        .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
    if (OperatingSystem.IsWindows() && builder.Configuration.GetValue("DataProtection:ProtectWithDpapi", true))
    {
        // DPAPI binds the key file to the Windows account running the site (the IIS pool identity).
        dataProtection.ProtectKeysWithDpapi();
    }
}
else if (isProduction)
{
    throw new InvalidOperationException(
        "DataProtection:KeyDirectory must be configured in production so sessions survive restarts.");
}

// ---------------------------------------------------------------------------------------------
// Reverse proxy headers are honoured only for explicitly listed proxies; arbitrary clients cannot
// spoof X-Forwarded-For to dodge IP throttling. IIS in-process hosting needs no proxy entry.
// ---------------------------------------------------------------------------------------------
var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
if (knownProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in knownProxies)
        {
            if (System.Net.IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }
    });
}

var app = builder.Build();

// Role initialization is idempotent and gives policy names a real database backing.
await using (var startupScope = app.Services.CreateAsyncScope())
{
    // Diagnostics first: this command must report an unreachable database or pending migrations
    // instead of failing inside role initialization.
    if (args.Contains(EnvironmentCheck.CommandName, StringComparer.OrdinalIgnoreCase))
    {
        var check = startupScope.ServiceProvider.GetRequiredService<EnvironmentCheck>();
        Environment.ExitCode = await check.RunAsync(Console.Out);
        return;
    }

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

    if (args.Contains("bootstrap-accountant", StringComparer.OrdinalIgnoreCase))
    {
        var bootstrapper = startupScope.ServiceProvider
            .GetRequiredService<AccountantBootstrapper>();
        await bootstrapper.RunAsync();
        return;
    }

    // Load (or create) the key ring now, under the identity that will serve requests, so a
    // folder the pool cannot write to fails here with the folder and account named instead of
    // as an HTTP 500 at the first sign-in. Note what this does not catch: an existing key file
    // the pool cannot decrypt (DPAPI-bound to another account) is logged as an error by Data
    // Protection and replaced by a fresh key, which signs everyone out but does not stop the
    // start-up. The log line below is the evidence operators look for after a pool start.
    try
    {
        startupScope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PurchaseApproval.StartupProbe")
            .Protect("startup");
    }
    catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException
                                          or UnauthorizedAccessException or IOException or InvalidOperationException)
    {
        throw new InvalidOperationException(
            $"Data Protection keys could not be loaded or created (DataProtection:KeyDirectory = " +
            $"'{keyDirectory ?? "<not configured>"}', identity '{DataProtectionKeyDirectory.CurrentIdentity()}'). " +
            "Check the folder permissions; keys protected with DPAPI are readable only by the account " +
            "that created them on this machine.", exception);
    }

    app.Logger.LogInformation(
        "Data Protection key ring loaded from {KeyLocation} as {Identity}.",
        string.IsNullOrWhiteSpace(keyDirectory) ? "the default location (not persistent for IIS)" : keyDirectory,
        DataProtectionKeyDirectory.CurrentIdentity());
}

// ---------------------------------------------------------------------------------------------
// HTTP pipeline. Order matters: forwarded headers first, then error handling, HTTPS, routing,
// localization, authentication, rate limiting, authorization, endpoints.
// ---------------------------------------------------------------------------------------------
if (knownProxies.Length > 0)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    // The error page shows only a correlation ID; details stay in the server log.
    app.UseExceptionHandler("/Error");
    app.UseStatusCodePagesWithReExecute("/Error", "?status={0}");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Defensive response headers that cost nothing and block common browser-side attacks.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "same-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseRouting();
app.UseRequestLocalization(requestLocalization);
// Identify the caller before authorization decides which pages they may access.
app.UseAuthentication();

app.UseRateLimiter();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// Exposes the entry point to WebApplicationFactory-based tests.
public partial class Program;
