namespace ECommerce.Worker.Messaging;

public interface IProcessedMessageStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> WasProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(
        Guid messageId,
        string eventType,
        CancellationToken cancellationToken = default);
}
