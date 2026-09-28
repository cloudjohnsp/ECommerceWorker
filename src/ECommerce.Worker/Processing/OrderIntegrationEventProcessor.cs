using System.Net.Mail;
using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerce.Worker.Processing;

public sealed class OrderIntegrationEventProcessor(WorkerDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedEventTypes =
    [
        IntegrationEventTypes.OrderCreated,
        IntegrationEventTypes.OrderUpdated,
        IntegrationEventTypes.OrderDeleted,
        IntegrationEventTypes.OrderPaid,
        IntegrationEventTypes.PaymentFailed,
        IntegrationEventTypes.OrderRefunded,
        IntegrationEventTypes.OrderCancelled
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
        if (!SupportedEventTypes.Contains(eventType))
            throw new InvalidIntegrationEventException($"Unsupported integration event type '{eventType}'.");

        if (await dbContext.ConsumedIntegrationEvents.AnyAsync(
                message => message.MessageId == messageId,
                cancellationToken))
            return IntegrationEventProcessingResult.AlreadyProcessed;

        var payload = Deserialize(body);
        Validate(payload, eventType);

        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        try
        {
            await dbContext.AcquireInboxMessageLockAsync(messageId, cancellationToken);
            if (await dbContext.ConsumedIntegrationEvents.AnyAsync(
                    message => message.MessageId == messageId,
                    cancellationToken))
                return IntegrationEventProcessingResult.AlreadyProcessed;

            var projection = await dbContext.OrderProjections.FindAsync(
                [payload.OrderId],
                cancellationToken);

            if (eventType == IntegrationEventTypes.OrderCreated)
            {
                projection ??= new OrderProjection(
                    payload.OrderId,
                    payload.CustomerId,
                    payload.CustomerEmail!,
                    payload.Status,
                    payload.Total,
                    payload.OccurredAt);
                if (dbContext.Entry(projection).State == EntityState.Detached)
                    await dbContext.OrderProjections.AddAsync(projection, cancellationToken);
            }
            else if (projection is null)
            {
                throw new MissingOrderProjectionException(payload.OrderId);
            }

            projection.Apply(payload.Status, payload.Total, payload.OccurredAt);

            if (eventType == IntegrationEventTypes.OrderPaid &&
                !await dbContext.Invoices.AnyAsync(invoice => invoice.OrderId == payload.OrderId, cancellationToken))
            {
                await dbContext.Invoices.AddAsync(
                    new Invoice(payload.OrderId, payload.Total, payload.OccurredAt),
                    cancellationToken);
            }

            var (subject, notificationBody) = CreateNotification(eventType, payload);
            await dbContext.NotificationOutboxMessages.AddAsync(
                new NotificationOutboxMessage(
                    messageId,
                    projection.CustomerEmail,
                    subject,
                    notificationBody,
                    DateTimeOffset.UtcNow,
                    correlationId),
                cancellationToken);
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

    private static OrderIntegrationEventPayload Deserialize(ReadOnlyMemory<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize<OrderIntegrationEventPayload>(body.Span, JsonOptions)
                ?? throw new InvalidIntegrationEventException("Integration event payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidIntegrationEventException(
                "Integration event payload is not valid JSON.",
                exception);
        }
    }

    private static void Validate(OrderIntegrationEventPayload payload, string eventType)
    {
        if (payload.OrderId == Guid.Empty || payload.CustomerId == Guid.Empty)
            throw new InvalidIntegrationEventException("Order and customer IDs are required.");
        if (string.IsNullOrWhiteSpace(payload.Status) || payload.Total <= 0)
            throw new InvalidIntegrationEventException("Order status and a positive total are required.");
        if (payload.OccurredAt == default)
            throw new InvalidIntegrationEventException("Event occurrence date is required.");
        if (eventType == IntegrationEventTypes.OrderCreated &&
            (string.IsNullOrWhiteSpace(payload.CustomerEmail) ||
             !MailAddress.TryCreate(payload.CustomerEmail, out _)))
            throw new InvalidIntegrationEventException("A valid customer e-mail is required for order.created.");
    }

    private static (string Subject, string Body) CreateNotification(
        string eventType,
        OrderIntegrationEventPayload payload) => eventType switch
        {
            IntegrationEventTypes.OrderCreated => (
                $"Pedido {payload.OrderId} criado",
                $"Seu pedido foi criado e aguarda pagamento. Total: {payload.Total:C}."),
            IntegrationEventTypes.OrderUpdated => (
                $"Pedido {payload.OrderId} atualizado",
                $"Um item foi adicionado ao pedido. Novo total: {payload.Total:C}."),
            IntegrationEventTypes.OrderDeleted => (
                $"Pedido {payload.OrderId} removido",
                "O pedido foi removido."),
            IntegrationEventTypes.OrderPaid => (
                $"Pagamento confirmado para o pedido {payload.OrderId}",
                "Seu pagamento foi confirmado e a nota fiscal foi gerada."),
            IntegrationEventTypes.PaymentFailed => (
                $"Pagamento não aprovado para o pedido {payload.OrderId}",
                "Não foi possível aprovar o pagamento. Você pode tentar novamente."),
            IntegrationEventTypes.OrderRefunded => (
                $"Pedido {payload.OrderId} reembolsado",
                "O reembolso do pedido foi registrado."),
            IntegrationEventTypes.OrderCancelled => (
                $"Pedido {payload.OrderId} cancelado",
                "O pedido foi cancelado."),
            _ => throw new InvalidIntegrationEventException($"Unsupported event type '{eventType}'.")
        };

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(
        CancellationToken cancellationToken) => dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
}
