// This file verifies the administrator approval and rejection business rules without a database.
// xUnit supplies Fact and Theory attributes through the test project's global using.
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class ApprovalDecisionRulesTests
{
    private static readonly DateTimeOffset DecisionTime =
        new(2026, 10, 18, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Approve_ChangesStatusAndSnapshotsAdministrator()
    {
        var request = CreatePendingRequest();

        var decision = ApprovalDecisionRules.CreateDecision(
            request, 7, "  Mina Manager  ", ApprovalDecisionType.Approved,
            "text that belongs only to rejection", DecisionTime);

        Assert.Equal(PaymentRequestStatus.Approved, request.Status);
        Assert.Same(decision, request.Decision);
        Assert.Equal("Mina Manager", decision.DecidedByFullNameSnapshot);
        Assert.Equal(7, decision.DecidedByUserId);
        Assert.Equal(DecisionTime, decision.DecidedAtUtc);
        Assert.Null(decision.RejectionReason);
    }

    [Fact]
    public void Reject_TrimsOptionalReason()
    {
        var request = CreatePendingRequest();

        var decision = ApprovalDecisionRules.CreateDecision(
            request, 7, "Mina Manager", ApprovalDecisionType.Rejected,
            "  Missing invoice  ", DecisionTime);

        Assert.Equal(PaymentRequestStatus.Rejected, request.Status);
        Assert.Equal("Missing invoice", decision.RejectionReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_AllowsMissingReason(string? reason)
    {
        var request = CreatePendingRequest();
        var decision = ApprovalDecisionRules.CreateDecision(
            request, 7, "Mina Manager",
            ApprovalDecisionType.Rejected, reason, DecisionTime);

        Assert.Equal(PaymentRequestStatus.Rejected, request.Status);
        Assert.Null(decision.RejectionReason);
    }

    [Fact]
    public void Reject_RejectsReasonAboveDatabaseLimit()
    {
        var exception = Assert.Throws<ApprovalDecisionValidationException>(() =>
            ApprovalDecisionRules.CreateDecision(
                CreatePendingRequest(), 7, "Mina Manager",
                ApprovalDecisionType.Rejected,
                new string('x', ApprovalDecisionRules.MaximumRejectionReasonLength + 1),
                DecisionTime));

        Assert.Contains(exception.Errors,
            error => error.Contains("cannot exceed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PaymentRequestStatus.Approved)]
    [InlineData(PaymentRequestStatus.Rejected)]
    public void Decide_RejectsRequestWithFinalStatus(PaymentRequestStatus status)
    {
        var request = CreatePendingRequest();
        request.Status = status;

        var exception = Assert.Throws<ApprovalDecisionValidationException>(() =>
            ApprovalDecisionRules.CreateDecision(
                request, 7, "Mina Manager", ApprovalDecisionType.Approved,
                null, DecisionTime));

        Assert.Contains(exception.Errors,
            error => error.Contains("final decision", StringComparison.Ordinal));
    }

    [Fact]
    public void Decide_RejectsUnknownDecisionValue()
    {
        var exception = Assert.Throws<ApprovalDecisionValidationException>(() =>
            ApprovalDecisionRules.CreateDecision(
                CreatePendingRequest(), 7, "Mina Manager",
                (ApprovalDecisionType)99, null, DecisionTime));

        Assert.Contains(exception.Errors,
            error => error.Contains("valid approval decision", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "Mina Manager")]
    [InlineData(7, "")]
    [InlineData(7, "   ")]
    public void Decide_RejectsInvalidAdministrator(int administratorId, string fullName)
    {
        var exception = Assert.Throws<ApprovalDecisionValidationException>(() =>
            ApprovalDecisionRules.CreateDecision(
                CreatePendingRequest(), administratorId, fullName,
                ApprovalDecisionType.Approved, null, DecisionTime));

        Assert.Contains(exception.Errors,
            error => error.Contains("valid administrator", StringComparison.Ordinal));
    }

    /// <summary>Creates the smallest request state that is eligible for review.</summary>
    private static PaymentRequest CreatePendingRequest() => new()
    {
        Id = 15,
        ReferenceNumber = "PR-2026-TEST",
        Status = PaymentRequestStatus.Pending
    };
}
