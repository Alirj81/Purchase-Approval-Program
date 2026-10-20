// In-memory failure counters for both login forms: one per client IP and one per targeted
// account. Identity lockout already protects staff passwords; these counters also cover employee
// access keys and slow down distributed guessing against a single account from many addresses.
//
// Design notes:
//  - Counters live in IMemoryCache with a fixed window, so a restart clears them. The
//    deployment runs as one IIS process, which is why no shared store is needed.
//  - A successful login clears only the account counter. The IP counter is left alone so an
//    attacker on a shared office network cannot reset it by signing in to their own account.
//  - Account keys are normalized usernames, never raw access keys, so secrets never become keys.
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;

namespace PurchaseApproval.Services;

public interface ILoginThrottlingService
{
    bool IsBlocked(string? ipAddress, string? accountKey);

    void RecordFailure(string? ipAddress, string? accountKey);

    void RecordSuccess(string? ipAddress, string? accountKey);

    /// <summary>Builds a non-secret account key, for example "staff:ADMIN" or "employee:A.RAHIMI".</summary>
    static string AccountKey(string kind, string? username)
        => $"{kind}:{(username ?? string.Empty).Trim().ToUpperInvariant()}";
}

public sealed class LoginThrottlingService(
    IMemoryCache cache,
    IOptions<ApplicationLimitsOptions> limits) : ILoginThrottlingService
{
    private sealed class Counter
    {
        public int Failures;
    }

    public bool IsBlocked(string? ipAddress, string? accountKey)
    {
        var options = limits.Value.LoginThrottling;
        return Exceeds(IpKey(ipAddress), options.FailuresPerIp)
            || Exceeds(AccountCacheKey(accountKey), options.FailuresPerAccount);
    }

    public void RecordFailure(string? ipAddress, string? accountKey)
    {
        var options = limits.Value.LoginThrottling;
        Increment(IpKey(ipAddress), TimeSpan.FromMinutes(options.IpWindowMinutes));
        Increment(AccountCacheKey(accountKey), TimeSpan.FromMinutes(options.AccountWindowMinutes));
    }

    public void RecordSuccess(string? ipAddress, string? accountKey)
    {
        var key = AccountCacheKey(accountKey);
        if (key is not null)
        {
            cache.Remove(key);
        }
    }

    private bool Exceeds(string? key, int limit)
        => key is not null && limit > 0 && cache.TryGetValue(key, out Counter? counter)
            && counter is not null && Volatile.Read(ref counter.Failures) >= limit;

    private void Increment(string? key, TimeSpan window)
    {
        if (key is null)
        {
            return;
        }

        var counter = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = window;
            return new Counter();
        })!;
        Interlocked.Increment(ref counter.Failures);
    }

    private static string? IpKey(string? ipAddress)
        => string.IsNullOrWhiteSpace(ipAddress) ? null : $"login-throttle:ip:{ipAddress}";

    private static string? AccountCacheKey(string? accountKey)
        => string.IsNullOrWhiteSpace(accountKey) ? null : $"login-throttle:account:{accountKey}";
}
