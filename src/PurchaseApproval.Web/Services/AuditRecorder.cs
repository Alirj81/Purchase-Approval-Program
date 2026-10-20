// Small factory that keeps every AuditEntry consistent: trimmed actor snapshot, server time,
// and a hard rule that Details never carries a secret. Services add the returned entity to the
// same DbContext as the change they describe so both are saved in one transaction.
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

/// <summary>Identity of the signed-in person performing an audited action.</summary>
public sealed record AuditActor(int? UserId, string FullName, string Role)
{
    public static readonly AuditActor System = new(null, "System", "System");
}

public static class AuditRecorder
{
    public static AuditEntry Create(
        AuditCategory category,
        AuditAction action,
        string subjectType,
        int subjectId,
        string subjectLabel,
        AuditActor actor,
        DateTimeOffset recordedAtUtc,
        string? details = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);

        return new AuditEntry
        {
            Category = category,
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            SubjectLabel = Truncate(subjectLabel.Trim(), 200),
            ActorUserId = actor.UserId,
            ActorFullNameSnapshot = Truncate(actor.FullName.Trim(), 200),
            ActorRoleSnapshot = Truncate(actor.Role.Trim(), 50),
            Details = string.IsNullOrWhiteSpace(details) ? null : Truncate(details.Trim(), 2000),
            RecordedAtUtc = recordedAtUtc
        };
    }

    private static string Truncate(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}
