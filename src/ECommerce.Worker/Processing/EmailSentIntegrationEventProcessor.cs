using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Worker.Processing;

public sealed class EmailSentIntegrationEventProcessor(WorkerDbContext dbContext)
{
    public const string EventType = IntegrationEventTypes.EmailSent;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedCategories =
    [
        EmailDeliveryCategories.EmailConfirmation,
        EmailDeliveryCategories.PasswordReset,
        EmailDeliveryCategories.OrderNotification
    ];

    public async Task<IntegrationEventProcessingResult> ProcessAsync(
        Guid messageId,
        string eventType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty)
            throw new InvalidIntegrationEventException("Integration event message ID is required.");
        if (!string.Equals(eventType, EventType, StringComparison.Ordinal))
            throw new InvalidIntegrationEventException(
                $"Unsupported e-mail integration event type '{eventType}'.");

        if (await IsConsumedAsync(messageId, cancellationToken))
            return IntegrationEventProcessingResult.AlreadyProcessed;

        var payload = Deserialize(body);
        Validate(payload);

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        try
        {
            await dbContext.AcquireInboxMessageLockAsync(messageId, cancellationToken);
            if (await IsConsumedAsync(messageId, cancellationToken))
                return IntegrationEventProcessingResult.AlreadyProcessed;

            var projection = await dbContext.EmailDeliveryProjections.FindAsync(
                [payload.DeliveryId],
                cancellationToken);
            if (projection is null)
            {
                projection = new EmailDeliveryProjection(
                    payload.DeliveryId,
                    payload.Category,
                    payload.OccurredAt);
                await dbContext.EmailDeliveryProjections.AddAsync(projection, cancellationToken);
            }
            else
            {
                projection.Apply(payload.Category, payload.OccurredAt);
            }

            await dbContext.ConsumedIntegrationEvents.AddAsync(
                new ConsumedIntegrationEvent(messageId, eventType, DateTimeOffset.UtcNow),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return IntegrationEventProcessingResult.Processed;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private Task<bool> IsConsumedAsync(Guid messageId, CancellationToken cancellationToken) =>
        dbContext.ConsumedIntegrationEvents.AnyAsync(
            message => message.MessageId == messageId,
            cancellationToken);

    private static EmailSentIntegrationEventPayload Deserialize(ReadOnlyMemory<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize<EmailSentIntegrationEventPayload>(body.Span, JsonOptions)
                ?? throw new InvalidIntegrationEventException(
                    "E-mail integration event payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidIntegrationEventException(
                "E-mail integration event payload is not valid JSON.",
                exception);
        }
    }

    private static void Validate(EmailSentIntegrationEventPayload payload)
    {
        if (payload.DeliveryId == Guid.Empty)
            throw new InvalidIntegrationEventException("Delivery ID is required.");
        if (!SupportedCategories.Contains(payload.Category))
            throw new InvalidIntegrationEventException(
                $"Unsupported e-mail delivery category '{payload.Category}'.");
        if (payload.OccurredAt == default)
            throw new InvalidIntegrationEventException("Event occurrence date is required.");
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(
        CancellationToken cancellationToken) => dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
}
