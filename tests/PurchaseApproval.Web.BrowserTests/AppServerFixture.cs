// Starts the real application as a separate process on a local port (Kestrel, "Testing"
// environment, test database) and a headless Chromium through Playwright. Seeding goes through
// the in-process TestWebApplicationFactory services so the browser tests reuse the same helpers
// as the HTTP tests. Screenshots are written to TestResults/browser for release verification.
using System.Diagnostics;
using Microsoft.Playwright;

namespace PurchaseApproval.Web.BrowserTests;

public sealed class AppServerFixture : IAsyncLifetime
{
    public const string BaseUrl = "http://127.0.0.1:5599";

    private Process? _server;
    private IPlaywright? _playwright;
    private IAsyncDisposable? _suiteLock;

    public IBrowser Browser { get; private set; } = null!;

    public IntegrationTests.TestWebApplicationFactory Seed { get; private set; } = null!;

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ScreenshotDirectory { get; } = Path.Combine(RepositoryRoot, "TestResults", "browser");

    public async Task InitializeAsync()
    {
        if (!IntegrationTests.TestDatabase.IsAvailable)
        {
            return;
        }

        // Waits here while the integration suite holds the shared test database.
        _suiteLock = await IntegrationTests.TestDatabase.AcquireSuiteLockAsync();
        await IntegrationTests.TestDatabase.MigrateAsync();
        Seed = new IntegrationTests.TestWebApplicationFactory();
        _ = Seed.Services;

        Directory.CreateDirectory(ScreenshotDirectory);
        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", Path.Combine(RepositoryRoot, ".playwright"));

        // The browser exercises the *published* output, exactly what IIS would serve: outside the
        // Development environment, fingerprinted static assets exist only in a publish folder.
        var publishDirectory = Path.Combine(RepositoryRoot, "TestResults", "publish-for-browser");
        var publish = Process.Start(new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList =
            {
                "publish",
                Path.Combine(RepositoryRoot, "src", "PurchaseApproval.Web", "PurchaseApproval.Web.csproj"),
                "--configuration", "Release", "--no-build", "--output", publishDirectory
            }
        }) ?? throw new InvalidOperationException("Could not start dotnet publish.");
        var publishOutput = await publish.StandardOutput.ReadToEndAsync();
        await publish.WaitForExitAsync();
        if (publish.ExitCode != 0)
        {
            throw new InvalidOperationException("dotnet publish failed: " + publishOutput);
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = publishDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(publishDirectory, "PurchaseApproval.Web.dll"));
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = IntegrationTests.TestDatabase.ConnectionString;
        startInfo.Environment["Limits__SubmissionBurstLimit"] = "100";
        _server = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the application.");
        // The server log is kept next to the screenshots so a failed step can be matched to its exception.
        var log = new StreamWriter(Path.Combine(ScreenshotDirectory, "server.log"), append: false) { AutoFlush = true };
        _server.OutputDataReceived += (_, args) => { if (args.Data is not null) lock (log) log.WriteLine(args.Data); };
        _server.ErrorDataReceived += (_, args) => { if (args.Data is not null) lock (log) log.WriteLine("ERR " + args.Data); };
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                var response = await probe.GetAsync(BaseUrl + "/");
                if ((int)response.StatusCode < 500)
                {
                    break;
                }
            }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
            }
            catch (TaskCanceledException) when (DateTime.UtcNow < deadline)
            {
            }

            if (_server.HasExited || DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("The application did not start within 90 seconds.");
            }

            await Task.Delay(500);
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        Seed?.Dispose();
        if (_server is { HasExited: false })
        {
            _server.Kill(entireProcessTree: true);
            _server.WaitForExit(10_000);
        }

        if (_suiteLock is not null)
        {
            await _suiteLock.DisposeAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PurchaseApproval.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>A fact that is skipped, and reported as such, when the test database is unavailable.</summary>
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (!IntegrationTests.TestDatabase.IsAvailable)
        {
            Skip = IntegrationTests.TestDatabase.SkipReason;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<AppServerFixture>
{
    public const string Name = "browser";
}
