using ECommerce.Worker.Contracts;

namespace ECommerce.Worker.Messaging;

public interface IOrderCreatedHandler
{
    Task HandleAsync(OrderCreatedEvent order, CancellationToken cancellationToken = default);
}
