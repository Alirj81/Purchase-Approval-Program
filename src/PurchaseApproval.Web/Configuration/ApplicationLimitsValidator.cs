// Validates the "Limits" section once at start-up (ValidateOnStart in Program.cs) so a typo in
// appsettings or an environment variable stops the site with a clear message instead of
// surfacing later as a quota of zero or an attachment ceiling above the database constraint.
// The same rules back the `environment-check` command, so operators see identical messages.
using Microsoft.Extensions.Options;

namespace PurchaseApproval.Configuration;

public sealed class ApplicationLimitsValidator : IValidateOptions<ApplicationLimitsOptions>
{
    /// <summary>Hard ceiling enforced by the PaymentRequestAttachments check constraint.</summary>
    public const long DatabaseAttachmentCeilingBytes = 5L * 1024 * 1024;

    private static readonly string[] VerifiableContentTypes =
        ["application/pdf", "image/jpeg", "image/png"];

    public ValidateOptionsResult Validate(string? name, ApplicationLimitsOptions options)
    {
        var failures = Describe(options).ToList();
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Every problem with the supplied values, each naming the configuration key to fix.</summary>
    public static IEnumerable<string> Describe(ApplicationLimitsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DailySubmissionQuota < 1)
        {
            yield return "Limits:DailySubmissionQuota must be at least 1.";
        }

        if (options.SubmissionBurstLimit < 1)
        {
            yield return "Limits:SubmissionBurstLimit must be at least 1.";
        }

        if (options.SubmissionBurstWindowMinutes < 1)
        {
            yield return "Limits:SubmissionBurstWindowMinutes must be at least 1.";
        }

        if (options.MaximumAttachmentCount is < 1 or > 20)
        {
            yield return "Limits:MaximumAttachmentCount must be between 1 and 20.";
        }

        if (options.MaximumAttachmentBytes < 1 || options.MaximumAttachmentBytes > DatabaseAttachmentCeilingBytes)
        {
            yield return $"Limits:MaximumAttachmentBytes must be between 1 and {DatabaseAttachmentCeilingBytes} " +
                         "(the database check constraint enforces the 5 MiB ceiling).";
        }

        // An empty list means "defaults"; configured entries must be types the sniffer can verify.
        foreach (var contentType in options.AllowedAttachmentContentTypes ?? [])
        {
            if (!VerifiableContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                yield return $"Limits:AllowedAttachmentContentTypes contains '{contentType}', which the application cannot verify by file signature (allowed: application/pdf, image/jpeg, image/png).";
            }
        }

        if (options.TransactionTimeFutureToleranceMinutes is < 0 or > 1440)
        {
            yield return "Limits:TransactionTimeFutureToleranceMinutes must be between 0 and 1440.";
        }

        if (options.TemporaryCredentialLifetimeHours is < 1 or > 720)
        {
            yield return "Limits:TemporaryCredentialLifetimeHours must be between 1 and 720.";
        }

        if (options.DefaultHistoryMonths < 1)
        {
            yield return "Limits:DefaultHistoryMonths must be at least 1.";
        }

        if (options.HistoryPageSize is < 1 or > 500)
        {
            yield return "Limits:HistoryPageSize must be between 1 and 500.";
        }

        var throttling = options.LoginThrottling ?? new LoginThrottlingOptions();
        if (throttling.FailuresPerIp < 1)
        {
            yield return "Limits:LoginThrottling:FailuresPerIp must be at least 1.";
        }

        if (throttling.IpWindowMinutes < 1)
        {
            yield return "Limits:LoginThrottling:IpWindowMinutes must be at least 1.";
        }

        if (throttling.FailuresPerAccount < 1)
        {
            yield return "Limits:LoginThrottling:FailuresPerAccount must be at least 1.";
        }

        if (throttling.AccountWindowMinutes < 1)
        {
            yield return "Limits:LoginThrottling:AccountWindowMinutes must be at least 1.";
        }
    }
}
