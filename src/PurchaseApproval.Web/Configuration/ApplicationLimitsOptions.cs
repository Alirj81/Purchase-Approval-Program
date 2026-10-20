// Strongly typed view of the "Limits" configuration section. Every operational limit the
// business asked to be configurable lives here so the deployment guide can state real values
// and operators change them in appsettings or environment variables, never in code.
// Bound once in Program.cs with builder.Services.Configure<ApplicationLimitsOptions>(...).
namespace PurchaseApproval.Configuration;

public sealed class ApplicationLimitsOptions
{
    public const string SectionName = "Limits";

    /// <summary>Successfully created requests allowed per account per UTC calendar day.</summary>
    public int DailySubmissionQuota { get; set; } = 10;

    /// <summary>Short-window burst guard: submission attempts (valid or not) per account.</summary>
    public int SubmissionBurstLimit { get; set; } = 5;

    public int SubmissionBurstWindowMinutes { get; set; } = 10;

    public int MaximumAttachmentCount { get; set; } = 5;

    /// <summary>5 MiB. The database check constraint on PaymentRequestAttachments enforces the same ceiling.</summary>
    public long MaximumAttachmentBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>
    /// Media types accepted after content sniffing, as configured; leave empty to accept the
    /// defaults. Only types the application can verify by file signature are valid (PDF, JPEG,
    /// PNG). The configuration binder appends configured values to whatever the array already
    /// holds, so the defaults live in DefaultAllowedAttachmentContentTypes rather than in this
    /// initializer; otherwise a narrower configured list could never remove a type.
    /// </summary>
    public string[] AllowedAttachmentContentTypes { get; set; } = [];

    public static readonly string[] DefaultAllowedAttachmentContentTypes =
        ["application/pdf", "image/jpeg", "image/png"];

    /// <summary>The allow-list actually enforced: configured types without duplicates, or the defaults when none are configured.</summary>
    public IReadOnlyList<string> EffectiveAllowedAttachmentContentTypes =>
        AllowedAttachmentContentTypes is { Length: > 0 }
            ? AllowedAttachmentContentTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : DefaultAllowedAttachmentContentTypes;

    /// <summary>Clock-skew allowance for a reported transaction time after the server recording time.</summary>
    public int TransactionTimeFutureToleranceMinutes { get; set; } = 15;

    /// <summary>How long a temporary staff setup/reset password stays usable.</summary>
    public int TemporaryCredentialLifetimeHours { get; set; } = 24;

    /// <summary>Set true for installations with more than one branch; Branch then becomes required on employee profiles.</summary>
    public bool RequireEmployeeBranch { get; set; }

    /// <summary>Default administrator history window in calendar months (view limit only, never deletion).</summary>
    public int DefaultHistoryMonths { get; set; } = 3;

    public int HistoryPageSize { get; set; } = 50;

    public LoginThrottlingOptions LoginThrottling { get; set; } = new();
}

/// <summary>
/// IP-based and account-targeted failure limits applied to both login forms. Identity lockout
/// still protects staff accounts; these counters close the gap for employee access keys and
/// slow down distributed guessing against any single account.
/// </summary>
public sealed class LoginThrottlingOptions
{
    public int FailuresPerIp { get; set; } = 20;

    public int IpWindowMinutes { get; set; } = 15;

    public int FailuresPerAccount { get; set; } = 10;

    public int AccountWindowMinutes { get; set; } = 15;
}
