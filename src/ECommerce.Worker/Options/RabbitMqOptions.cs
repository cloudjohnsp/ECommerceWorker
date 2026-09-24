using System.ComponentModel.DataAnnotations;

namespace ECommerce.Worker.Options;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required] public string HostName { get; init; } = "localhost";
    [Range(1, 65535)] public int Port { get; init; } = 5672;
    [Required] public string UserName { get; init; } = "guest";
    [Required] public string Password { get; init; } = "guest";
    [Required] public string VirtualHost { get; init; } = "/";
    [Required] public string ClientProvidedName { get; init; } = "ecommerce-worker";
    [Required] public string ExchangeName { get; init; } = "ecommerce.events";
    [Required] public string QueueName { get; init; } = "ecommerce.worker.orders";
    [Required] public string DeadLetterExchangeName { get; init; } = "ecommerce.events.dead-letter";
    [Required] public string DeadLetterQueueName { get; init; } = "ecommerce.worker.orders.dead-letter";
    [Range(1, 1000)] public ushort PrefetchCount { get; init; } = 10;
    [Range(1, 300)] public int ReconnectDelaySeconds { get; init; } = 5;
}
