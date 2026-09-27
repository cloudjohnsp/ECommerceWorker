namespace ECommerce.Worker.Persistence.Entities;

public sealed class OrderProjection
{
    private OrderProjection()
    {
    }

    public OrderProjection(
        Guid orderId,
        Guid customerId,
        string customerEmail,
        string status,
        decimal total,
        DateTimeOffset updatedAt)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID is required.", nameof(orderId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(customerEmail))
            throw new ArgumentException("Customer e-mail is required.", nameof(customerEmail));
        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Order status is required.", nameof(status));
        if (total <= 0)
            throw new ArgumentOutOfRangeException(nameof(total), "Order total must be positive.");

        OrderId = orderId;
        CustomerId = customerId;
        CustomerEmail = customerEmail.Trim().ToLowerInvariant();
        Status = status.Trim();
        Total = total;
        UpdatedAt = updatedAt;
    }

    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string CustomerEmail { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Apply(string status, decimal total, DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
            return;

        Status = status;
        Total = total;
        UpdatedAt = occurredAt;
    }
}
