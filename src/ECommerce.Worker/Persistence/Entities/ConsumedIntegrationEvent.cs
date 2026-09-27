namespace ECommerce.Worker.Persistence.Entities;

public sealed class ConsumedIntegrationEvent
{
    private ConsumedIntegrationEvent()
    {
    }

    public ConsumedIntegrationEvent(Guid messageId, string eventType, DateTimeOffset consumedAt)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("Message ID is required.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        MessageId = messageId;
        EventType = eventType.Trim();
        ConsumedAt = consumedAt;
    }

    public Guid MessageId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset ConsumedAt { get; private set; }
}
