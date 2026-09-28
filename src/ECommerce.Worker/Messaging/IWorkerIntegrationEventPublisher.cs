using ECommerce.Worker.Contracts;

namespace ECommerce.Worker.Messaging;

public sealed record WorkerIntegrationEvent(
    Guid Id,
    string Type,
    string Payload,
    DateTimeOffset OccurredAt,
    string? CorrelationId = null,
    string Version = IntegrationEventContract.Version1);

public interface IWorkerIntegrationEventPublisher
{
    Task PublishAsync(
        WorkerIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default);
}
