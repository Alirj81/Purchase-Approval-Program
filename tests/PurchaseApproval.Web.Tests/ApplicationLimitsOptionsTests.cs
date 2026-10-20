// Configuration binding and validation of the Limits section. The allow-list test guards a
// binder subtlety found during the production-configuration review: arrays are appended to,
// not replaced, so defaults must not live in the property initializer.
using Microsoft.Extensions.Configuration;
using PurchaseApproval.Configuration;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class ApplicationLimitsOptionsTests
{
    [Fact]
    public void WithoutConfiguredContentTypes_TheDefaultAllowListApplies()
    {
        var options = Bind([]);

        Assert.Equal(ApplicationLimitsOptions.DefaultAllowedAttachmentContentTypes, options.EffectiveAllowedAttachmentContentTypes);
        Assert.Empty(ApplicationLimitsValidator.Describe(options));
        Assert.Contains("image/png", AttachmentPolicy.FromOptions(options).AllowedContentTypes);
    }

    [Fact]
    public void ConfiguredContentTypes_ReplaceTheDefaultsInsteadOfAppendingToThem()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Limits:AllowedAttachmentContentTypes:0"] = "application/pdf",
            ["Limits:AllowedAttachmentContentTypes:1"] = "application/pdf"
        });

        Assert.Equal(["application/pdf"], options.EffectiveAllowedAttachmentContentTypes);
        var policy = AttachmentPolicy.FromOptions(options);
        Assert.Contains("application/pdf", policy.AllowedContentTypes);
        Assert.DoesNotContain("image/png", policy.AllowedContentTypes);
        Assert.DoesNotContain("image/jpeg", policy.AllowedContentTypes);
    }

    [Fact]
    public void Validator_NamesEveryInvalidSetting()
    {
        var options = new ApplicationLimitsOptions
        {
            DailySubmissionQuota = 0,
            SubmissionBurstWindowMinutes = 0,
            MaximumAttachmentBytes = 6L * 1024 * 1024,
            AllowedAttachmentContentTypes = ["application/zip"],
            TemporaryCredentialLifetimeHours = 0,
            LoginThrottling = new LoginThrottlingOptions { FailuresPerIp = 0 }
        };

        var problems = ApplicationLimitsValidator.Describe(options).ToList();

        Assert.Equal(6, problems.Count);
        Assert.Contains(problems, problem => problem.StartsWith("Limits:DailySubmissionQuota", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Limits:SubmissionBurstWindowMinutes", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Limits:MaximumAttachmentBytes", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("application/zip", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Limits:TemporaryCredentialLifetimeHours", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("Limits:LoginThrottling:FailuresPerIp", StringComparison.Ordinal));
    }

    [Fact]
    public void Defaults_AreValid()
    {
        Assert.Empty(ApplicationLimitsValidator.Describe(new ApplicationLimitsOptions()));
    }

    private static ApplicationLimitsOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new ApplicationLimitsOptions();
        configuration.GetSection(ApplicationLimitsOptions.SectionName).Bind(options);
        return options;
    }
}
