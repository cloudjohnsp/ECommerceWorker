using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Options;
using ECommerce.Worker.Observability;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using ECommerce.Worker.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Notifications;

public sealed class NotificationOutboxProcessor(
    WorkerDbContext dbContext,
    IOrderEmailSender emailSender,
    IOptions<NotificationProcessorOptions> options,
    ILogger<NotificationOutboxProcessor> logger)
{
    private readonly NotificationProcessorOptions _options = options.Value;

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = await ClaimBatchAsync(cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                await emailSender.SendAsync(
                    message.Recipient,
                    message.Subject,
                    message.Body,
                    cancellationToken);
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
                WorkerTelemetry.FailedNotifications.Add(1);
                logger.LogWarning(
                    exception,
                    "Notification {NotificationId} delivery failed on attempt {Attempt}.",
                    message.Id,
                    message.Attempts);
                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            var sentAt = DateTimeOffset.UtcNow;
            message.MarkSent(sentAt);
            var payload = new EmailSentIntegrationEventPayload(
                message.Id,
                EmailDeliveryCategories.OrderNotification,
                sentAt);
            await dbContext.IntegrationOutboxMessages.AddAsync(
                new WorkerIntegrationOutboxMessage(
                    message.Id,
                    EmailSentIntegrationEventProcessor.EventType,
                    JsonSerializer.Serialize(payload),
                    sentAt,
                    message.CorrelationId),
                cancellationToken);
            WorkerTelemetry.SentNotifications.Add(1);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return messages.Count;
    }

    private async Task<List<NotificationOutboxMessage>> ClaimBatchAsync(
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
        var messages = await dbContext.NotificationOutboxMessages
            .FromSqlInterpolated($$"""
                SELECT *
                FROM worker.notification_outbox_messages
                WHERE "SentAt" IS NULL
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

    private IQueryable<NotificationOutboxMessage> PendingQuery(DateTimeOffset now) =>
        dbContext.NotificationOutboxMessages
            .Where(message =>
                message.SentAt == null &&
                message.Attempts < _options.MaximumAttempts &&
                message.NextAttemptAt <= now &&
                (message.LockedUntil == null || message.LockedUntil <= now))
            .OrderBy(message => message.CreatedAt);
}
