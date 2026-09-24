using ECommerce.Worker.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ECommerce.Worker.Messaging;

public sealed class RabbitMqOrderConsumer(
    ConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    IProcessedMessageStore processedMessageStore,
    OrderCreatedMessageProcessor processor,
    ILogger<RabbitMqOrderConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeStoreWithRetryAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "RabbitMQ consumer stopped unexpectedly. Reconnecting in {DelaySeconds} seconds.",
                    _options.ReconnectDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            await HandleDeliveryAsync(channel, eventArgs, stoppingToken);
        };
        await channel.BasicConsumeAsync(
            _options.QueueName,
            autoAck: false,
            consumer,
            stoppingToken);

        logger.LogInformation(
            "Listening to queue {QueueName} on exchange {ExchangeName}.",
            _options.QueueName,
            _options.ExchangeName);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
    {
        if (!Guid.TryParse(eventArgs.BasicProperties.MessageId, out var messageId))
        {
            logger.LogWarning("Rejecting RabbitMQ message without a valid MessageId.");
            await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        try
        {
            var eventType = eventArgs.BasicProperties.Type ?? eventArgs.RoutingKey;
            var result = await processor.ProcessAsync(
                messageId,
                eventType,
                eventArgs.Body,
                stoppingToken);

            if (result == MessageProcessingResult.Invalid)
            {
                logger.LogWarning("Rejecting invalid event {MessageId} of type {EventType}.", messageId, eventType);
                await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false, stoppingToken);
                return;
            }

            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
            logger.LogInformation(
                result == MessageProcessingResult.Duplicate
                    ? "Acknowledged duplicate event {MessageId}."
                    : "Processed event {MessageId}.",
                messageId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to process event {MessageId}; message will be retried.", messageId);
            await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            _options.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            _options.DeadLetterExchangeName,
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName
        };
        await channel.QueueDeclareAsync(
            _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.QueueName,
            _options.ExchangeName,
            "order.created",
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            _options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.DeadLetterQueueName,
            _options.DeadLetterExchangeName,
            routingKey: string.Empty,
            arguments: null,
            cancellationToken: cancellationToken);
    }

    private async Task InitializeStoreWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await processedMessageStore.InitializeAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Inbox database is unavailable. Retrying in {DelaySeconds} seconds.",
                    _options.ReconnectDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), cancellationToken);
            }
        }
    }
}
