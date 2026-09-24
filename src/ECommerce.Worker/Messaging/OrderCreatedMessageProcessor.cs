using System.Text.Json;
using ECommerce.Worker.Contracts;

namespace ECommerce.Worker.Messaging;

public sealed class OrderCreatedMessageProcessor(
    IProcessedMessageStore processedMessageStore,
    IOrderCreatedHandler handler)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<MessageProcessingResult> ProcessAsync(
        Guid messageId,
        string eventType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(eventType, "order.created", StringComparison.Ordinal))
            return MessageProcessingResult.Invalid;
        if (await processedMessageStore.WasProcessedAsync(messageId, cancellationToken))
            return MessageProcessingResult.Duplicate;

        OrderCreatedEvent? order;
        try
        {
            order = JsonSerializer.Deserialize<OrderCreatedEvent>(body.Span, SerializerOptions);
        }
        catch (JsonException)
        {
            return MessageProcessingResult.Invalid;
        }

        if (order is null || order.Id == Guid.Empty || order.CustomerId == Guid.Empty || order.Items.Count == 0)
            return MessageProcessingResult.Invalid;

        await handler.HandleAsync(order, cancellationToken);
        await processedMessageStore.MarkProcessedAsync(messageId, eventType, cancellationToken);
        return MessageProcessingResult.Completed;
    }
}
