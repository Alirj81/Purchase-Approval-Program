// This file owns the business rules and database transaction for approving or rejecting a request.
// Entity Framework Core supplies the tracked request and detects stale concurrent updates.
// Each decision also writes an AuditEntry (see AuditRecorder) and, when the deciding administrator
// is the request's own administrator requester, sets the persisted self-approval indicator.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;
using PurchaseApproval.Security;

namespace PurchaseApproval.Services;

/// <summary>Records one final administrator decision for a pending payment request.</summary>
public interface IApprovalDecisionService
{
    Task<ApprovalDecisionResult> DecideAsync(
        int requestId,
        uint expectedVersion,
        int administratorId,
        string administratorFullName,
        ApprovalDecisionType decisionType,
        string? rejectionReason,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes the expected outcomes of a decision attempt without exposing database errors.</summary>
public enum ApprovalDecisionResult
{
    Success,
    RequestNotFound,
    AlreadyDecided,
    ConcurrencyConflict
}

/// <summary>Contains validation messages that can safely be shown on the review page.</summary>
public sealed class ApprovalDecisionValidationException(IReadOnlyList<LocalizableMessage> messages)
    : ValidationMessagesException("The approval decision is invalid.", messages);

/// <summary>Loads a request, applies the domain rules, and saves the request and decision atomically.</summary>
public sealed class ApprovalDecisionService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : IApprovalDecisionService
{
    /// <summary>
    /// Saves a final decision only when the version submitted by the review page is still current.
    /// </summary>
    public async Task<ApprovalDecisionResult> DecideAsync(
        int requestId,
        uint expectedVersion,
        int administratorId,
        string administratorFullName,
        ApprovalDecisionType decisionType,
        string? rejectionReason,
        CancellationToken cancellationToken = default)
    {
        var request = await dbContext.PaymentRequests
            .Include(item => item.Decision)
            .Include(item => item.TransferOutcome)
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);

        if (request is null)
        {
            return ApprovalDecisionResult.RequestNotFound;
        }

        if (request.Status != PaymentRequestStatus.Pending || request.Decision is not null)
        {
            return ApprovalDecisionResult.AlreadyDecided;
        }

        // The hidden version protects against approving data changed after the page was opened.
        if (request.Version != expectedVersion)
        {
            return ApprovalDecisionResult.ConcurrencyConflict;
        }

        var decidedAtUtc = timeProvider.GetUtcNow();
        var decision = ApprovalDecisionRules.CreateDecision(
            request,
            administratorId,
            administratorFullName,
            decisionType,
            rejectionReason,
            decidedAtUtc);

        dbContext.ApprovalDecisions.Add(decision);
        if (decisionType == ApprovalDecisionType.Approved)
        {
            var transfer = TransferExecutionRules.CreateAwaitingTransfer(
                request,
                administratorId,
                administratorFullName,
                AppRoles.Administrator,
                decidedAtUtc);
            dbContext.TransferOutcomes.Add(transfer);
        }

        dbContext.AuditEntries.Add(ApprovalDecisionRules.CreateAudit(request, decision));
        dbContext.Entry(request).Property(item => item.Version).OriginalValue = expectedVersion;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ApprovalDecisionResult.Success;
        }
        catch (DbUpdateException exception)
        {
            var failure = DatabaseErrorClassifier.Classify(exception);
            switch (failure.Kind)
            {
                // A second administrator completed the request between the read and the save.
                case DatabaseFailureKind.ConcurrencyConflict:
                    return ApprovalDecisionResult.ConcurrencyConflict;
                // The one-decision-per-request index fired: the other decision won.
                case DatabaseFailureKind.UniqueViolation:
                    return ApprovalDecisionResult.AlreadyDecided;
                case DatabaseFailureKind.ConstraintViolation:
                    throw new ApprovalDecisionValidationException(
                        [DatabaseErrorClassifier.DescribeConstraintViolation()]);
                default:
                    // Unexpected failures reach the global error handler, which logs a correlation ID.
                    throw;
            }
        }
    }
}

/// <summary>Pure decision rules kept separate so important behavior can be unit tested quickly.</summary>
public static class ApprovalDecisionRules
{
    public const int MaximumRejectionReasonLength = 2000;

    /// <summary>Validates and creates the decision while updating the request's status.</summary>
    public static ApprovalDecision CreateDecision(
        PaymentRequest request,
        int administratorId,
        string administratorFullName,
        ApprovalDecisionType decisionType,
        string? rejectionReason,
        DateTimeOffset decidedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<LocalizableMessage>();
        var cleanAdministratorName = administratorFullName?.Trim() ?? string.Empty;
        var cleanReason = string.IsNullOrWhiteSpace(rejectionReason)
            ? null
            : rejectionReason.Trim();

        if (request.Status != PaymentRequestStatus.Pending || request.Decision is not null)
        {
            errors.Add("This payment request already has a final decision.");
        }

        if (!Enum.IsDefined(decisionType))
        {
            errors.Add("Choose a valid approval decision.");
        }

        if (administratorId <= 0 || cleanAdministratorName.Length is 0 or > 200)
        {
            errors.Add("A valid administrator is required.");
        }

        if (cleanReason?.Length > MaximumRejectionReasonLength)
        {
            errors.Add(new LocalizableMessage(
                "The rejection reason cannot exceed {0} characters.", MaximumRejectionReasonLength));
        }

        if (errors.Count != 0)
        {
            throw new ApprovalDecisionValidationException(errors);
        }

        // Approval never stores text entered in the rejection-only field.
        if (decisionType == ApprovalDecisionType.Approved)
        {
            cleanReason = null;
        }

        request.Status = decisionType == ApprovalDecisionType.Approved
            ? PaymentRequestStatus.Approved
            : PaymentRequestStatus.Rejected;

        var decision = new ApprovalDecision
        {
            PaymentRequest = request,
            DecisionType = decisionType,
            DecidedByUserId = administratorId,
            DecidedByFullNameSnapshot = cleanAdministratorName,
            RejectionReason = cleanReason,
            // Determined from authenticated identities only; the browser never supplies it.
            IsSelfApproval = IsSelfDecision(request, administratorId),
            DecidedAtUtc = decidedAtUtc
        };

        request.Decision = decision;
        return decision;
    }

    /// <summary>True when an administrator decides a request they submitted themselves.</summary>
    public static bool IsSelfDecision(PaymentRequest request, int administratorId)
        => request.RequesterType == RequesterType.Administrator
            && request.ApplicationUserRequesterId == administratorId;

    /// <summary>Builds the accountability row saved together with the decision.</summary>
    public static AuditEntry CreateAudit(PaymentRequest request, ApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        var action = decision.IsSelfApproval && decision.DecisionType == ApprovalDecisionType.Approved
            ? AuditAction.RequestSelfApproved
            : decision.DecisionType == ApprovalDecisionType.Approved
                ? AuditAction.RequestApproved
                : AuditAction.RequestRejected;

        var details = decision.IsSelfApproval
            ? $"{decision.DecisionType}; the deciding administrator is also the request's requester (self-approval)."
            : $"{decision.DecisionType}; requester type {request.RequesterType}.";

        return AuditRecorder.Create(
            AuditCategory.ApprovalDecision,
            action,
            nameof(PaymentRequest),
            request.Id,
            request.ReferenceNumber,
            new AuditActor(decision.DecidedByUserId, decision.DecidedByFullNameSnapshot, AppRoles.Administrator),
            decision.DecidedAtUtc,
            details);
    }
}
