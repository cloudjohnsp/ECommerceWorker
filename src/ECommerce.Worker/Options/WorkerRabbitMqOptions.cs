namespace ECommerce.Worker.Options;

public sealed class WorkerRabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string VirtualHost { get; init; } = "/";
    public string ClientProvidedName { get; init; } = "ecommerce-worker";
    public string ExchangeName { get; init; } = "ecommerce.events";
    public string QueueName { get; init; } = "ecommerce.worker.orders";
    public string DeadLetterExchangeName { get; init; } = "ecommerce.events.dlx";
    public string DeadLetterQueueName { get; init; } = "ecommerce.worker.orders.dead";
    public int DeliveryLimit { get; init; } = 5;
    public int RetryDelaySeconds { get; init; } = 15;
    public int ReconnectDelaySeconds { get; init; } = 5;
}
