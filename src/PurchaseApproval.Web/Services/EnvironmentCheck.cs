// `dotnet PurchaseApproval.Web.dll environment-check` prints what the deployment guide asks an
// operator to verify on the server, in the exact runtime that will serve the site: runtime and
// ICU state, IANA time-zone resolution, the Data Protection key folder, database reachability
// and migration state, and the effective configuration. It prints no secrets and changes
// nothing except one temporary probe file in the key folder. Exit code 1 means at least one
// FAIL line was printed. It deliberately does not create Data Protection keys: a key created by
// the operator's account would be DPAPI-bound to that account, not to the application pool.
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PurchaseApproval.Configuration;
using PurchaseApproval.Data;
using PurchaseApproval.Security;

namespace PurchaseApproval.Services;

public sealed class EnvironmentCheck(
    IConfiguration configuration,
    IHostEnvironment environment,
    ApplicationDbContext dbContext,
    IOptions<ApplicationLimitsOptions> limits)
{
    public const string CommandName = "environment-check";

    /// <summary>Zones the check resolves; Asia/Tehran is the expected office zone, the others prove general ICU support.</summary>
    public static readonly string[] SampleIanaZones =
        ["UTC", "Asia/Tehran", "Europe/Berlin", "Europe/London", "America/New_York", "Asia/Dubai"];

    private TextWriter _output = TextWriter.Null;
    private bool _failed;

    public async Task<int> RunAsync(TextWriter output, CancellationToken cancellationToken = default)
    {
        _output = output;
        _failed = false;

        Section("Runtime");
        Ok($"Environment: {environment.EnvironmentName}");
        Ok($"Operating system: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Ok($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Ok($"Process identity: {DataProtectionKeyDirectory.CurrentIdentity()}");
        Ok($"Machine time zone: {TimeZoneInfo.Local.Id}; UTC now {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
        var invariantMode = AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) && invariant;
        if (invariantMode)
        {
            Fail("Globalization invariant mode is enabled; IANA time zones and culture data need ICU. Remove InvariantGlobalization or DOTNET_SYSTEM_GLOBALIZATION_INVARIANT.");
        }
        else
        {
            Ok("Globalization: ICU available (invariant mode off).");
        }

        Section("IANA time zones (used for reported transaction times and report boundaries)");
        foreach (var zoneId in SampleIanaZones)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                var offset = zone.GetUtcOffset(DateTimeOffset.UtcNow);
                Ok($"{zoneId} resolves (current offset {(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm}).");
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                Fail($"{zoneId} could not be resolved: {exception.Message}. Install ICU/time-zone data on this server.");
            }
        }

        Section("Data Protection keys (sign-in cookies and anti-forgery tokens)");
        var keyDirectory = configuration["DataProtection:KeyDirectory"];
        if (string.IsNullOrWhiteSpace(keyDirectory))
        {
            if (environment.IsProduction())
            {
                Fail("DataProtection:KeyDirectory is not configured; the site refuses to start in Production.");
            }
            else
            {
                Warn("DataProtection:KeyDirectory is not configured; keys live in the user profile or memory (acceptable outside Production).");
            }
        }
        else
        {
            var problem = DataProtectionKeyDirectory.Probe(keyDirectory);
            if (problem is null)
            {
                var keyFiles = Directory.GetFiles(keyDirectory, "key-*.xml").Length;
                Ok($"Key folder '{keyDirectory}' exists and is writable by {DataProtectionKeyDirectory.CurrentIdentity()}; {keyFiles} key file(s) present.");
                var dpapi = OperatingSystem.IsWindows() && configuration.GetValue("DataProtection:ProtectWithDpapi", true);
                if (dpapi)
                {
                    Warn("Keys are DPAPI-protected to the Windows account that creates them. Existing keys can only be confirmed readable by the application-pool identity itself: check the site's start-up log line 'Data Protection key ring loaded' after the pool starts.");
                }
                else
                {
                    Ok("DPAPI protection is off; key files are protected only by folder permissions.");
                }
            }
            else
            {
                Fail(problem);
            }
        }

        Section("Database");
        await CheckDatabaseAsync(cancellationToken);

        Section("Configuration");
        var allowedHosts = configuration["AllowedHosts"];
        if (environment.IsProduction() && (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*"))
        {
            Warn("AllowedHosts is '*'; set it to the real host name(s) so the site answers only for its own name.");
        }
        else
        {
            Ok($"AllowedHosts: {allowedHosts}");
        }

        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        Ok(proxies.Length == 0
            ? "ForwardedHeaders: none (correct for IIS in-process hosting without a reverse proxy; client addresses are real)."
            : $"ForwardedHeaders: X-Forwarded-For/Proto honoured only from {string.Join(", ", proxies)}.");

        try
        {
            var values = limits.Value;
            Ok($"Limits: {values.DailySubmissionQuota} requests per account per UTC day; burst {values.SubmissionBurstLimit} attempts per {values.SubmissionBurstWindowMinutes} min; " +
               $"attachments {values.MaximumAttachmentCount} x {values.MaximumAttachmentBytes} bytes ({string.Join(", ", values.EffectiveAllowedAttachmentContentTypes)}); " +
               $"transaction-time tolerance {values.TransactionTimeFutureToleranceMinutes} min; temporary password {values.TemporaryCredentialLifetimeHours} h; " +
               $"login throttling {values.LoginThrottling.FailuresPerIp}/IP per {values.LoginThrottling.IpWindowMinutes} min and {values.LoginThrottling.FailuresPerAccount}/account per {values.LoginThrottling.AccountWindowMinutes} min; " +
               $"employee branch required: {values.RequireEmployeeBranch}; history {values.DefaultHistoryMonths} months, {values.HistoryPageSize} rows per page.");
        }
        catch (OptionsValidationException exception)
        {
            foreach (var failure in exception.Failures)
            {
                Fail(failure);
            }
        }

        Ok("Sessions: 8 hours sliding for staff and employees; the security stamp is validated on every request so deactivation, reset or role change ends a session immediately.");
        Ok(environment.IsProduction()
            ? "Production: cookies are Secure-only, HSTS is sent, errors show a reference only."
            : $"{environment.EnvironmentName}: cookies follow the request scheme; production guards are off.");

        _output.WriteLine();
        _output.WriteLine(_failed ? "RESULT: FAIL (see lines marked FAIL)" : "RESULT: OK");
        return _failed ? 1 : 0;
    }

    private async Task CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Fail("ConnectionStrings:DefaultConnection is not configured.");
            return;
        }

        NpgsqlConnectionStringBuilder target;
        try
        {
            target = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            Fail($"ConnectionStrings:DefaultConnection could not be parsed: {exception.Message}");
            return;
        }

        Ok($"Target: host {target.Host}, port {target.Port}, database '{target.Database}', user '{target.Username}', SSL mode {target.SslMode} (password not shown).");
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            Ok($"Connected; PostgreSQL server version {connection.ServerVersion}.");
            await connection.CloseAsync();

            var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            Ok(applied.Count == 0
                ? "Applied migrations: none (empty database)."
                : $"Applied migrations: {applied.Count}, latest {applied[^1]}.");
            if (pending.Count > 0)
            {
                Fail($"Pending migrations: {string.Join(", ", pending)}. Back up the database, then run the migration bundle before starting the site.");
            }
            else
            {
                Ok("No pending migrations; the schema matches this build.");
            }
        }
        catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException
                                              or TimeoutException or InvalidOperationException)
        {
            Fail($"Database check failed ({exception.GetType().Name}): {exception.Message}");
        }
    }

    private void Section(string title)
    {
        _output.WriteLine();
        _output.WriteLine($"== {title}");
    }

    private void Ok(string message) => _output.WriteLine($"[OK]   {message}");

    private void Warn(string message) => _output.WriteLine($"[WARN] {message}");

    private void Fail(string message)
    {
        _failed = true;
        _output.WriteLine($"[FAIL] {message}");
    }
}
