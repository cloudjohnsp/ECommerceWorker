namespace ECommerce.Worker.Persistence.Entities;

public sealed class Invoice
{
    private Invoice()
    {
    }

    public Invoice(Guid orderId, decimal amount, DateTimeOffset issuedAt)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID is required.", nameof(orderId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Invoice amount must be positive.");

        Id = Guid.NewGuid();
        OrderId = orderId;
        Number = $"INV-{issuedAt:yyyyMMdd}-{orderId:N}".ToUpperInvariant();
        Amount = amount;
        IssuedAt = issuedAt;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
}
