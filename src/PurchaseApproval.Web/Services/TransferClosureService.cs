// Closes overdue execution items from immutable server-recorded outcome timestamps.
// The hosted worker runs independently of browser activity and processes overdue rows at startup.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public interface ITransferClosureProcessor
{
    Task<int> CloseOverdueAsync(CancellationToken cancellationToken = default);
}

public sealed class TransferClosureProcessor(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : ITransferClosureProcessor
{
    private const int BatchSize = 200;

    public async Task<int> CloseOverdueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var transferredCutoff = now - TransferExecutionRules.TransferredClosureDelay;
        var unsuccessfulCutoff = now - TransferExecutionRules.UnsuccessfulClosureDelay;

        var dueIds = await dbContext.TransferOutcomes
            .AsNoTracking()
            .Where(item => !item.IsClosed
                && item.RecordedAtUtc != null
                && ((item.Status == TransferStatus.Transferred
                        && item.RecordedAtUtc <= transferredCutoff)
                    || (item.Status == TransferStatus.Unsuccessful
                        && item.RecordedAtUtc <= unsuccessfulCutoff)))
            .OrderBy(item => item.RecordedAtUtc)
            .Select(item => item.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var closedCount = 0;
        foreach (var id in dueIds)
        {
            dbContext.ChangeTracker.Clear();
            var transfer = await dbContext.TransferOutcomes
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (transfer is null || transfer.IsClosed || transfer.RecordedAtUtc is null
                || TransferExecutionRules.GetAutomaticClosureDueAt(transfer) > now)
            {
                continue;
            }

            TransferExecutionRules.ApplyAutomaticClosure(transfer, now);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                closedCount++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker or an administrator closed the same row first.
            }
        }

        return closedCount;
    }
}

public sealed class TransferClosureHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<TransferClosureHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessOnceAsync(stoppingToken);
        }
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<ITransferClosureProcessor>();
            var closed = await processor.CloseOverdueAsync(cancellationToken);
            if (closed > 0)
            {
                logger.LogInformation(
                    "Automatically closed {ClosedExecutionItemCount} overdue execution items.",
                    closed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Automatic execution-item closure failed; it will retry.");
        }
    }
}
