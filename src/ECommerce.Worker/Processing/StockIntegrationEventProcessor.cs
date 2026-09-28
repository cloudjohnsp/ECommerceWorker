using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Worker.Processing;

public sealed class StockIntegrationEventProcessor(WorkerDbContext dbContext)
{
    public const string EventType = IntegrationEventTypes.StockUpdated;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedReasons =
    [
        StockUpdateReasons.Created,
        StockUpdateReasons.Adjusted,
        StockUpdateReasons.Reserved,
        StockUpdateReasons.ReservationConsumed,
        StockUpdateReasons.ReservationReleased,
        StockUpdateReasons.Restored
    ];

    public async Task<IntegrationEventProcessingResult> ProcessAsync(
        Guid messageId,
        string eventType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default,
        string? correlationId = null)
    {
        if (messageId == Guid.Empty)
            throw new InvalidIntegrationEventException("Integration event message ID is required.");
        if (!string.Equals(eventType, EventType, StringComparison.Ordinal))
            throw new InvalidIntegrationEventException(
                $"Unsupported stock integration event type '{eventType}'.");

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

            var projection = await dbContext.InventoryProjections.FindAsync(
                [payload.ProductId],
                cancellationToken);
            if (projection is null)
            {
                projection = new InventoryProjection(
                    payload.ProductId,
                    payload.AvailableStock,
                    payload.OrderId,
                    payload.Reason,
                    payload.OccurredAt);
                await dbContext.InventoryProjections.AddAsync(projection, cancellationToken);
            }
            else
            {
                projection.Apply(
                    payload.AvailableStock,
                    payload.OrderId,
                    payload.Reason,
                    payload.OccurredAt);
            }

            await dbContext.ConsumedIntegrationEvents.AddAsync(
                new ConsumedIntegrationEvent(messageId, eventType, DateTimeOffset.UtcNow),
                cancellationToken);
            await dbContext.AnalyticsEvents.AddAsync(
                new AnalyticsEvent(messageId, eventType, payload.OccurredAt, correlationId),
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

    private static StockUpdatedIntegrationEventPayload Deserialize(ReadOnlyMemory<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize<StockUpdatedIntegrationEventPayload>(
                    body.Span,
                    JsonOptions)
                ?? throw new InvalidIntegrationEventException(
                    "Stock integration event payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidIntegrationEventException(
                "Stock integration event payload is not valid JSON.",
                exception);
        }
    }

    private static void Validate(StockUpdatedIntegrationEventPayload payload)
    {
        if (payload.ProductId == Guid.Empty)
            throw new InvalidIntegrationEventException("Product ID is required.");
        if (payload.AvailableStock < 0)
            throw new InvalidIntegrationEventException("Available stock cannot be negative.");
        if (!SupportedReasons.Contains(payload.Reason))
            throw new InvalidIntegrationEventException(
                $"Unsupported stock update reason '{payload.Reason}'.");
        if (payload.OccurredAt == default)
            throw new InvalidIntegrationEventException("Event occurrence date is required.");
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(
        CancellationToken cancellationToken) => dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
}
