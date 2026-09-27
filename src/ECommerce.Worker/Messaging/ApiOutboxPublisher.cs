using ECommerce.Worker.Contracts;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Messaging;

public sealed class ApiOutboxPublisher(
    WorkerDbContext dbContext,
    IWorkerIntegrationEventPublisher publisher,
    IOptions<ApiOutboxPublisherOptions> options,
    ILogger<ApiOutboxPublisher> logger)
{
    private readonly ApiOutboxPublisherOptions _options = options.Value;

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = await ClaimBatchAsync(cancellationToken);
        foreach (var message in messages)
        {
            var lockId = message.LockId!.Value;
            try
            {
                var eventType = message.GetEventType()
                    ?? throw new InvalidOperationException($"Unsupported API outbox type {message.Type}.");
                await publisher.PublishAsync(
                    new WorkerIntegrationEvent(message.Id, eventType, message.Payload, message.CreatedAt),
                    cancellationToken);
                await MarkProcessedAsync(message, lockId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await MarkFailedAsync(message, lockId, exception.Message, cancellationToken);
                logger.LogWarning(
                    exception,
                    "API outbox message {MessageId} publication failed on attempt {Attempt}.",
                    message.Id,
                    message.Attempts);
            }
        }

        return messages.Count;
    }

    private async Task<List<ApiOutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lockId = Guid.NewGuid();
        if (!dbContext.Database.IsRelational())
        {
            var inMemoryMessages = await PendingQuery(now)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var message in inMemoryMessages)
                message.Claim(lockId, now.AddSeconds(_options.LockSeconds));
            await dbContext.SaveChangesAsync(cancellationToken);
            return inMemoryMessages;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var messages = await dbContext.ApiOutboxMessages
            .FromSqlInterpolated($$"""
                SELECT *
                FROM public."OutboxMessages"
                WHERE "Status" = {{ApiOutboxMessage.PendingStatus}}
                  AND "Type" IN (1, 2, 3, 7, 8, 9, 10, 11, 12)
                  AND "Attempts" < {{_options.MaximumAttempts}}
                  AND "NextAttemptAt" <= {{now}}
                  AND ("LockedUntil" IS NULL OR "LockedUntil" <= {{now}})
                ORDER BY "CreatedAt"
                LIMIT {{_options.BatchSize}}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
            message.Claim(lockId, now.AddSeconds(_options.LockSeconds));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    private IQueryable<ApiOutboxMessage> PendingQuery(DateTimeOffset now) =>
        dbContext.ApiOutboxMessages
            .Where(message =>
                message.Status == ApiOutboxMessage.PendingStatus &&
                (message.Type == 1 || message.Type == 2 || message.Type == 3 ||
                 message.Type == 7 || message.Type == 8 || message.Type == 9 ||
                 message.Type == 10 || message.Type == 11 || message.Type == 12) &&
                message.Attempts < _options.MaximumAttempts &&
                message.NextAttemptAt <= now &&
                (message.LockedUntil == null || message.LockedUntil <= now))
            .OrderBy(message => message.CreatedAt);

    private async Task MarkProcessedAsync(
        ApiOutboxMessage message,
        Guid lockId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!dbContext.Database.IsRelational())
        {
            message.MarkProcessed(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var updated = await dbContext.ApiOutboxMessages
            .Where(candidate => candidate.Id == message.Id && candidate.LockId == lockId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.Status, ApiOutboxMessage.ProcessedStatus)
                .SetProperty(candidate => candidate.ProcessedAt, now)
                .SetProperty(candidate => candidate.UpdatedAt, now)
                .SetProperty(candidate => candidate.LockId, (Guid?)null)
                .SetProperty(candidate => candidate.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.LastError, (string?)null), cancellationToken);
        if (updated == 0)
            logger.LogWarning("API outbox message {MessageId} was published after its lease was lost.", message.Id);
    }

    private async Task MarkFailedAsync(
        ApiOutboxMessage message,
        Guid lockId,
        string error,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var delaySeconds = Math.Min(
            Math.Pow(2, message.Attempts - 1) * _options.BaseRetryDelaySeconds,
            3600);
        var nextAttemptAt = now.AddSeconds(delaySeconds);
        message.MarkFailed(error, nextAttemptAt, now);
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.ApiOutboxMessages
            .Where(candidate => candidate.Id == message.Id && candidate.LockId == lockId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.NextAttemptAt, nextAttemptAt)
                .SetProperty(candidate => candidate.UpdatedAt, now)
                .SetProperty(candidate => candidate.LockId, (Guid?)null)
                .SetProperty(candidate => candidate.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.LastError, message.LastError), cancellationToken);
    }
}
