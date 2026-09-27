namespace ECommerce.Worker.Notifications;

public sealed class DisabledOrderEmailSender(ILogger<DisabledOrderEmailSender> logger) : IOrderEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "E-mail delivery is disabled. Notification to {Recipient} with subject {Subject} was acknowledged.",
            recipient,
            subject);
        return Task.CompletedTask;
    }
}
