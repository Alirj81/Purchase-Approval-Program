// Enforces the per-account daily submission quota atomically inside the caller's transaction.
//
// How: one SubmissionQuotaDays row per (requester type, requester ID, UTC date). On PostgreSQL a
// single INSERT ... ON CONFLICT ... DO UPDATE ... WHERE count < limit both creates/increments the
// row and locks it until the surrounding transaction commits, so two simultaneous submissions
// cannot both see "9 of 10". When the row is not returned the quota is full. The non-relational
// branch keeps the same contract for EF InMemory unit tests, where no transaction exists.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public interface ISubmissionQuotaService
{
    /// <summary>Reserves one unit of today's quota; returns false when the limit is already reached.</summary>
    Task<bool> TryReserveAsync(
        RequesterType requesterType,
        int requesterId,
        DateTimeOffset nowUtc,
        int dailyLimit,
        CancellationToken cancellationToken = default);
}

public sealed class SubmissionQuotaService(ApplicationDbContext dbContext) : ISubmissionQuotaService
{
    public async Task<bool> TryReserveAsync(
        RequesterType requesterType,
        int requesterId,
        DateTimeOffset nowUtc,
        int dailyLimit,
        CancellationToken cancellationToken = default)
    {
        if (dailyLimit <= 0)
        {
            return false;
        }

        // UTC calendar date is the shared quota-day boundary for every requester.
        var quotaDate = DateOnly.FromDateTime(nowUtc.UtcDateTime);

        if (dbContext.Database.IsNpgsql())
        {
            var reserved = await dbContext.Database
                .SqlQuery<int>($"""
                    INSERT INTO "SubmissionQuotaDays" ("RequesterType", "RequesterId", "QuotaDateUtc", "CreatedCount")
                    VALUES ({(int)requesterType}, {requesterId}, {quotaDate}, 1)
                    ON CONFLICT ("RequesterType", "RequesterId", "QuotaDateUtc")
                    DO UPDATE SET "CreatedCount" = "SubmissionQuotaDays"."CreatedCount" + 1
                    WHERE "SubmissionQuotaDays"."CreatedCount" < {dailyLimit}
                    RETURNING "CreatedCount" AS "Value"
                    """)
                .ToListAsync(cancellationToken);
            return reserved.Count == 1;
        }

        // Non-relational fallback (unit tests): same semantics without row locking.
        var row = await dbContext.SubmissionQuotaDays.SingleOrDefaultAsync(
            item => item.RequesterType == requesterType
                && item.RequesterId == requesterId
                && item.QuotaDateUtc == quotaDate,
            cancellationToken);
        if (row is null)
        {
            dbContext.SubmissionQuotaDays.Add(new SubmissionQuotaDay
            {
                RequesterType = requesterType,
                RequesterId = requesterId,
                QuotaDateUtc = quotaDate,
                CreatedCount = 1
            });
            return true;
        }

        if (row.CreatedCount >= dailyLimit)
        {
            return false;
        }

        row.CreatedCount++;
        return true;
    }
}
