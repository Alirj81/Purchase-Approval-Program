using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class PaymentRequestSubmissionRulesTests
{
    private static readonly EmployeeProfile Requester = new(
        42, "Current Employee", "employee42", "09120000000",
        "Operations", "Main", "Coordinator");

    // Represents the signed-in administrator submitting their own internal payment request.
    private static readonly ApplicationUserProfile AdministratorRequester = new(
        9, "Approval Admin", "approval.admin", "09121111111",
        "Management", "Head Office", "Administrator");

    [Fact]
    public void CreateRequest_RecalculatesTotalAndSnapshotsRequester()
    {
        var submittedAt = new DateTimeOffset(2026, 10, 17, 8, 30, 0, TimeSpan.Zero);

        var request = PaymentRequestSubmissionRules.CreateRequest(
            Requester,
            [new(" Paper ", 250_000, true, " Invoice 1 "), new("Ink", 750_000, false, null)],
            [],
            " Supplier ",
            " Office supplies ",
            null,
            "PR-2026-TEST",
            new string('A', 64),
            submittedAt);

        Assert.Equal(1_000_000, request.TotalAmountRials);
        Assert.Equal(42, request.EmployeeRequesterId);
        Assert.Equal(RequesterType.Employee, request.RequesterType);
        Assert.Equal("Current Employee", request.RequesterFullNameSnapshot);
        Assert.Equal("Supplier", request.PayeeName);
        Assert.Equal(PaymentRequestStatus.Pending, request.Status);
        Assert.Equal([0, 1], request.Lines.Select(line => line.DisplayOrder));
    }

    [Fact]
    public void CreateRequest_ForAdministratorLinksApplicationUserAndSnapshotsProfile()
    {
        var request = PaymentRequestSubmissionRules.CreateRequest(
            AdministratorRequester,
            [new("Urgent supplies", 500_000, true, null)],
            [],
            "Supplier",
            "Office supplies",
            null,
            "PR-2026-ADMIN",
            new string('B', 64),
            DateTimeOffset.UtcNow);

        Assert.Equal(RequesterType.Administrator, request.RequesterType);
        Assert.Null(request.EmployeeRequesterId);
        Assert.Equal(AdministratorRequester.Id, request.ApplicationUserRequesterId);
        Assert.Equal(AdministratorRequester.FullName, request.RequesterFullNameSnapshot);
        Assert.Equal(AdministratorRequester.Username, request.RequesterUsernameSnapshot);
        Assert.Equal(PaymentRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void CreateRequest_RejectsTotalAboveDatabaseLimit()
    {
        var exception = Assert.Throws<PaymentRequestValidationException>(() =>
            Create([new("Expense", PaymentRequestSubmissionRules.MaximumTotalRials + 1, false, null)]));

        Assert.Contains(exception.Errors, error => error.Contains("cannot exceed", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateRequest_AllowsExactMaximumWithNoAttachments()
    {
        var request = Create(
            [new("Expense", PaymentRequestSubmissionRules.MaximumTotalRials, false, null)],
            []);

        Assert.Equal(PaymentRequestSubmissionRules.MaximumTotalRials, request.TotalAmountRials);
        Assert.Empty(request.Attachments);
    }

    [Fact]
    public void CreateRequest_RejectsMissingAndInvalidLines()
    {
        var noLines = Assert.Throws<PaymentRequestValidationException>(() => Create([]));
        var invalidAmount = Assert.Throws<PaymentRequestValidationException>(() =>
            Create([new("Expense", 0, false, null)]));

        Assert.Contains(noLines.Errors, error => error.Contains("between 1", StringComparison.Ordinal));
        Assert.Contains(invalidAmount.Errors, error => error.Contains("greater than zero", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    public void CreateRequest_DetectsAttachmentTypeFromContent(string kind, string expectedType)
    {
        var content = kind switch
        {
            "pdf" => "%PDF-1.7 test"u8.ToArray(),
            "jpeg" => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
            _ => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
        };

        var request = Create(
            [new("Expense", 1, false, null)],
            [new("evidence.bin", "application/octet-stream", content)]);

        var attachment = Assert.Single(request.Attachments);
        Assert.Equal(expectedType, attachment.ContentType);
        Assert.Equal(64, attachment.Sha256Hash.Length);
    }

    [Fact]
    public void CreateRequest_RejectsSpoofedAttachment()
    {
        var exception = Assert.Throws<PaymentRequestValidationException>(() => Create(
            [new("Expense", 1, false, null)],
            [new("fake.pdf", "application/pdf", "not a pdf"u8.ToArray())]));

        Assert.Contains(exception.Errors, error => error.Contains("PDF, JPEG, or PNG", StringComparison.Ordinal));
    }

    [Fact]
    public void TrackingToken_VerifiesOnlyMatchingToken()
    {
        var token = PaymentRequestSubmissionRules.CreateTrackingToken();
        var hash = PaymentRequestSubmissionRules.HashTrackingToken(token);

        Assert.True(PaymentRequestSubmissionRules.VerifyTrackingToken(token, hash));
        Assert.False(PaymentRequestSubmissionRules.VerifyTrackingToken(token + "x", hash));
        Assert.False(PaymentRequestSubmissionRules.VerifyTrackingToken(token, "invalid"));
    }

    [Fact]
    public void ReferencesAndTokens_AreUniqueAndHaveExpectedShape()
    {
        var now = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        var references = Enumerable.Range(0, 100)
            .Select(_ => PaymentRequestSubmissionRules.CreateReference(now)).ToHashSet();
        var tokens = Enumerable.Range(0, 100)
            .Select(_ => PaymentRequestSubmissionRules.CreateTrackingToken()).ToHashSet();

        Assert.Equal(100, references.Count);
        Assert.All(references, value => Assert.StartsWith("PR-2026-", value, StringComparison.Ordinal));
        Assert.Equal(100, tokens.Count);
        Assert.All(tokens, value => Assert.DoesNotContain("=", value, StringComparison.Ordinal));
    }

    [Fact]
    public void SubmissionKey_IsHighEntropyAndHashesDeterministically()
    {
        var key = PaymentRequestSubmissionRules.CreateSubmissionKey();

        Assert.DoesNotContain("=", key, StringComparison.Ordinal);
        Assert.Equal(64, PaymentRequestSubmissionRules.HashSubmissionKey(key).Length);
        Assert.Equal(
            PaymentRequestSubmissionRules.HashSubmissionKey(key),
            PaymentRequestSubmissionRules.HashSubmissionKey(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void SubmissionKey_RejectsInvalidValue(string key)
    {
        Assert.Throws<PaymentRequestValidationException>(() =>
            PaymentRequestSubmissionRules.HashSubmissionKey(key));
    }

    private static PaymentRequest Create(
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput>? attachments = null)
        => PaymentRequestSubmissionRules.CreateRequest(
            Requester, lines, attachments ?? [], "Supplier", "Reason", null,
            "PR-TEST", new string('A', 64), DateTimeOffset.UtcNow);
}
