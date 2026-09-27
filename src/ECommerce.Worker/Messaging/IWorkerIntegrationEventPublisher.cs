namespace ECommerce.Worker.Messaging;

public sealed record WorkerIntegrationEvent(
    Guid Id,
    string Type,
    string Payload,
    DateTimeOffset OccurredAt);

public interface IWorkerIntegrationEventPublisher
{
    Task PublishAsync(
        WorkerIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default);
}
