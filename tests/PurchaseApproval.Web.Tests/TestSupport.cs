// Small shared helpers for unit tests: a fixed clock, a pass-through localizer and an InMemory
// DbContext factory. Kept minimal so each test file still reads on its own.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
using PurchaseApproval.Data;

namespace PurchaseApproval.Web.Tests;

internal sealed class FixedClock(DateTimeOffset value) : TimeProvider
{
    public DateTimeOffset Value { get; set; } = value;

    public override DateTimeOffset GetUtcNow() => Value;
}

/// <summary>Returns the key itself (optionally formatted), which is exactly the English fallback behaviour.</summary>
internal sealed class PassThroughLocalizer : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments]
        => new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

internal static class TestSupport
{
    public static ApplicationDbContext CreateInMemoryContext(string? name = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    public static IOptions<ApplicationLimitsOptions> Limits(Action<ApplicationLimitsOptions>? configure = null)
    {
        var options = new ApplicationLimitsOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }
}
