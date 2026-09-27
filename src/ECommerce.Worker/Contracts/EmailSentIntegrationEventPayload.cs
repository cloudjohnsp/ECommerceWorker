namespace ECommerce.Worker.Contracts;

public static class EmailDeliveryCategories
{
    public const string EmailConfirmation = "email_confirmation";
    public const string PasswordReset = "password_reset";
    public const string OrderNotification = "order_notification";
}

public sealed record EmailSentIntegrationEventPayload(
    Guid DeliveryId,
    string Category,
    DateTimeOffset OccurredAt);
