namespace ECommerce.Worker.Notifications;

public interface IOrderEmailSender
{
    Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
