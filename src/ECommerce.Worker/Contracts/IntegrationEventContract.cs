namespace ECommerce.Worker.Contracts;

public static class IntegrationEventContract
{
    public const string VersionHeaderName = "x-contract-version";
    public const string Version1 = "1";

    public static bool IsSupportedVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ||
        string.Equals(version, Version1, StringComparison.Ordinal);
}

public static class IntegrationEventTypes
{
    public const string OrderCreated = "order.created";
    public const string OrderUpdated = "order.updated";
    public const string OrderPaid = "order.paid";
    public const string PaymentFailed = "payment.failed";
    public const string OrderRefunded = "order.refunded";
    public const string OrderCancelled = "order.cancelled";
    public const string StockUpdated = "stock.updated";
    public const string EmailSent = "email.sent";
}
