namespace ECommerce.Worker.Persistence.Entities;

public sealed class AnalyticsEvent
{
    private AnalyticsEvent()
    {
    }

    public AnalyticsEvent(
        Guid messageId,
        string eventType,
        DateTimeOffset occurredAt,
        string? correlationId)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("Message ID is required.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));
        if (occurredAt == default)
            throw new ArgumentException("Occurrence date is required.", nameof(occurredAt));

        MessageId = messageId;
        EventType = eventType.Trim();
        OccurredAt = occurredAt;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? messageId.ToString("N")
            : correlationId.Trim();
    }

    public Guid MessageId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
}
