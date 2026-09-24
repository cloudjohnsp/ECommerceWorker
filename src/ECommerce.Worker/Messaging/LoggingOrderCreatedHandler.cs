using ECommerce.Worker.Contracts;
using Microsoft.Extensions.Logging;

namespace ECommerce.Worker.Messaging;

public sealed class LoggingOrderCreatedHandler(ILogger<LoggingOrderCreatedHandler> logger) : IOrderCreatedHandler
{
    public Task HandleAsync(OrderCreatedEvent order, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Order {OrderId} received. Simulating confirmation e-mail for customer {CustomerId}.",
            order.Id,
            order.CustomerId);
        logger.LogInformation(
            "Simulating invoice generation for order {OrderId}, total {OrderTotal} and {ItemCount} items.",
            order.Id,
            order.Total,
            order.Items.Count);
        return Task.CompletedTask;
    }
}
