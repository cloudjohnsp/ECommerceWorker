using ECommerce.Worker.Observability;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Messaging;

public sealed class WorkerIntegrationEventOutboxProcessor(
    WorkerDbContext dbContext,
    IWorkerIntegrationEventPublisher publisher,
    IOptions<NotificationProcessorOptions> options,
    ILogger<WorkerIntegrationEventOutboxProcessor> logger)
{
    private readonly NotificationProcessorOptions _options = options.Value;

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = await ClaimBatchAsync(cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(
                    new WorkerIntegrationEvent(
                        message.Id,
                        message.Type,
                        message.Payload,
                        message.CreatedAt),
                    cancellationToken);
                message.MarkProcessed(DateTimeOffset.UtcNow);
                WorkerTelemetry.PublishedEvents.Add(1);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var retryDelay = TimeSpan.FromSeconds(
                    Math.Min(Math.Pow(2, message.Attempts + 1) * 5, 3600));
                message.MarkFailed(exception.Message, DateTimeOffset.UtcNow.Add(retryDelay));
                WorkerTelemetry.FailedEventPublications.Add(1);
                logger.LogWarning(
                    exception,
                    "Integration event {MessageId} publication failed on attempt {Attempt}.",
                    message.Id,
                    message.Attempts);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return messages.Count;
    }

    private async Task<List<WorkerIntegrationOutboxMessage>> ClaimBatchAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!dbContext.Database.IsRelational())
        {
            var inMemoryMessages = await PendingQuery(now)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var message in inMemoryMessages)
                message.Lock(now.AddSeconds(_options.LockSeconds));
            await dbContext.SaveChangesAsync(cancellationToken);
            return inMemoryMessages;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var messages = await dbContext.IntegrationOutboxMessages
            .FromSqlInterpolated($$"""
                SELECT *
                FROM worker.integration_outbox_messages
                WHERE "ProcessedAt" IS NULL
                  AND "Attempts" < {{_options.MaximumAttempts}}
                  AND "NextAttemptAt" <= {{now}}
                  AND ("LockedUntil" IS NULL OR "LockedUntil" <= {{now}})
                ORDER BY "CreatedAt"
                LIMIT {{_options.BatchSize}}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
            message.Lock(now.AddSeconds(_options.LockSeconds));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    private IQueryable<WorkerIntegrationOutboxMessage> PendingQuery(DateTimeOffset now) =>
        dbContext.IntegrationOutboxMessages
            .Where(message =>
                message.ProcessedAt == null &&
                message.Attempts < _options.MaximumAttempts &&
                message.NextAttemptAt <= now &&
                (message.LockedUntil == null || message.LockedUntil <= now))
            .OrderBy(message => message.CreatedAt);
}
