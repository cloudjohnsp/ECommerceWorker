namespace ECommerce.Worker.Persistence.Entities;

public sealed class EmailDeliveryProjection
{
    private EmailDeliveryProjection()
    {
    }

    public EmailDeliveryProjection(Guid deliveryId, string category, DateTimeOffset sentAt)
    {
        Validate(deliveryId, category, sentAt);
        DeliveryId = deliveryId;
        Category = category.Trim();
        SentAt = sentAt;
    }

    public Guid DeliveryId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public DateTimeOffset SentAt { get; private set; }

    public void Apply(string category, DateTimeOffset sentAt)
    {
        Validate(DeliveryId, category, sentAt);
        if (sentAt < SentAt)
            return;

        Category = category.Trim();
        SentAt = sentAt;
    }

    private static void Validate(Guid deliveryId, string category, DateTimeOffset sentAt)
    {
        if (deliveryId == Guid.Empty)
            throw new ArgumentException("Delivery ID is required.", nameof(deliveryId));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Delivery category is required.", nameof(category));
        if (sentAt == default)
            throw new ArgumentException("Delivery date is required.", nameof(sentAt));
    }
}
