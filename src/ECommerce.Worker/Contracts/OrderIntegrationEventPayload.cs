namespace ECommerce.Worker.Contracts;

public sealed record OrderIntegrationEventItem(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);

public sealed record OrderIntegrationEventPayload(
    Guid OrderId,
    Guid CustomerId,
    string? CustomerEmail,
    string Status,
    decimal Total,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<OrderIntegrationEventItem> Items);
