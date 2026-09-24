namespace ECommerce.Worker.Contracts;

public sealed record OrderCreatedEvent(
    Guid Id,
    Guid CustomerId,
    int Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<OrderCreatedItem> Items);

public sealed record OrderCreatedItem(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);
