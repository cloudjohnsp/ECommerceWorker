namespace ECommerce.Worker.Contracts;

public static class StockUpdateReasons
{
    public const string Created = "created";
    public const string Adjusted = "adjusted";
    public const string Reserved = "reserved";
    public const string ReservationConsumed = "reservation_consumed";
    public const string ReservationReleased = "reservation_released";
    public const string Restored = "restored";
}

public sealed record StockUpdatedIntegrationEventPayload(
    Guid ProductId,
    int AvailableStock,
    Guid? OrderId,
    string Reason,
    DateTimeOffset OccurredAt);
