// Locates the disposable PostgreSQL test database and refuses anything else.
//
// The connection string comes only from the PURCHASE_APPROVAL_TEST_CONNECTION environment
// variable; .env, user secrets and appsettings are never consulted, so a developer cannot point
// these tests at purchase_approval_dev by accident. The database name must end in "_test".
// When the variable is absent every [PostgresFact] is reported as skipped, never as passed.
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PurchaseApproval.Data;

namespace PurchaseApproval.Web.IntegrationTests;

public static class TestDatabase
{
    public const string EnvironmentVariable = "PURCHASE_APPROVAL_TEST_CONNECTION";

    public const string SkipReason =
        "PURCHASE_APPROVAL_TEST_CONNECTION is not set; PostgreSQL integration test skipped (not verified).";

    private static readonly Lazy<string?> Resolved = new(Resolve);

    public static string? ConnectionString => Resolved.Value;

    public static bool IsAvailable => ConnectionString is not null;

    private static string? Resolve()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(value);
        if (string.IsNullOrWhiteSpace(builder.Database)
            || !builder.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariable} must name a disposable database ending in '_test'; " +
                $"'{builder.Database}' is refused to protect development and production data.");
        }

        return value;
    }

    // Both database-backed suites (integration and browser) truncate this one database. A
    // session-level PostgreSQL advisory lock, held by each suite's collection fixture for the
    // whole run, makes them execute one after the other even when `dotnet test` on the solution
    // starts both assemblies at once. The dedicated, unpooled connection keeps the lock bound to
    // one physical session; closing it releases the lock even if the process dies.
    private const long SuiteLockKey = 7_272_026;

    public static async Task<IAsyncDisposable> AcquireSuiteLockAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            ConnectionString ?? throw new InvalidOperationException(SkipReason))
        {
            Pooling = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection) { CommandTimeout = 0 })
        {
            command.Parameters.AddWithValue("key", SuiteLockKey);
            await command.ExecuteNonQueryAsync();
        }

        return new SuiteLock(connection);
    }

    private sealed class SuiteLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock_all()", connection);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    public static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString ?? throw new InvalidOperationException(SkipReason))
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>Applies pending migrations to the test database (never to any other database).</summary>
    public static async Task MigrateAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Empties every application table in the test database; roles are kept.</summary>
    public static async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "AuditEntries",
                "SubmissionQuotaDays",
                "TransferAuditEntries",
                "TransferOutcomes",
                "ApprovalDecisions",
                "PaymentRequestAttachments",
                "PaymentRequestLines",
                "PaymentRequests",
                "Employees",
                "AspNetUserRoles",
                "AspNetUserClaims",
                "AspNetUserLogins",
                "AspNetUserTokens",
                "ApplicationUsers",
                "ItemCatalogueEntries"
            RESTART IDENTITY CASCADE;
            """);
    }
}

/// <summary>A fact that is skipped, and reported as such, when the test database is unavailable.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!TestDatabase.IsAvailable)
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}

/// <summary>
/// Holds the suite lock for the whole run and migrates the test database once; tests reset
/// tables individually.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private IAsyncDisposable? _suiteLock;

    public async Task InitializeAsync()
    {
        if (TestDatabase.IsAvailable)
        {
            _suiteLock = await TestDatabase.AcquireSuiteLockAsync();
            await TestDatabase.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_suiteLock is not null)
        {
            await _suiteLock.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "postgres";
}
