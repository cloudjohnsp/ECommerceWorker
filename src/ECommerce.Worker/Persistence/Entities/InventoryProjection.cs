namespace ECommerce.Worker.Persistence.Entities;

public sealed class InventoryProjection
{
    private InventoryProjection()
    {
    }

    public InventoryProjection(
        Guid productId,
        int availableStock,
        Guid? lastOrderId,
        string lastReason,
        DateTimeOffset updatedAt)
    {
        Validate(productId, availableStock, lastReason, updatedAt);
        ProductId = productId;
        AvailableStock = availableStock;
        LastOrderId = lastOrderId;
        LastReason = lastReason.Trim();
        UpdatedAt = updatedAt;
    }

    public Guid ProductId { get; private set; }
    public int AvailableStock { get; private set; }
    public Guid? LastOrderId { get; private set; }
    public string LastReason { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Apply(
        int availableStock,
        Guid? orderId,
        string reason,
        DateTimeOffset occurredAt)
    {
        Validate(ProductId, availableStock, reason, occurredAt);
        if (occurredAt < UpdatedAt)
            return;

        AvailableStock = availableStock;
        LastOrderId = orderId;
        LastReason = reason.Trim();
        UpdatedAt = occurredAt;
    }

    private static void Validate(
        Guid productId,
        int availableStock,
        string reason,
        DateTimeOffset occurredAt)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product ID is required.", nameof(productId));
        if (availableStock < 0)
            throw new ArgumentOutOfRangeException(
                nameof(availableStock),
                "Available stock cannot be negative.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Stock update reason is required.", nameof(reason));
        if (occurredAt == default)
            throw new ArgumentException("Update date is required.", nameof(occurredAt));
    }
}
