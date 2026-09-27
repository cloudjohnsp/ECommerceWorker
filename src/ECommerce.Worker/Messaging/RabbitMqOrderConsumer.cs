using System.Diagnostics;
using System.Text;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Observability;
using ECommerce.Worker.Options;
using ECommerce.Worker.Processing;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ECommerce.Worker.Messaging;

public sealed class RabbitMqOrderConsumer(
    ConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerRabbitMqOptions> options,
    ILogger<RabbitMqOrderConsumer> logger) : BackgroundService
{
    private readonly WorkerRabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilDisconnectedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RabbitMQ consumer disconnected; reconnecting.");
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.ReconnectDelaySeconds),
                    stoppingToken);
            }
        }
    }

    private async Task ConsumeUntilDisconnectedAsync(CancellationToken stoppingToken)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        var disconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
            await HandleDeliveryAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(
            _options.QueueName,
            autoAck: false,
            consumer,
            stoppingToken);

        logger.LogInformation("Consuming RabbitMQ queue {QueueName}.", _options.QueueName);
        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        await Task.WhenAny(disconnected.Task, cancelled);
        stoppingToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("RabbitMQ connection was closed.");
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        CancellationToken stoppingToken)
    {
        using var activity = WorkerTelemetry.ActivitySource.StartActivity(
            "rabbitmq.consume",
            ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", _options.QueueName);
        activity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);
        activity?.SetTag("messaging.event.type", delivery.BasicProperties.Type);

        try
        {
            if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
                throw new InvalidIntegrationEventException("RabbitMQ MessageId must be a GUID.");
            if (string.IsNullOrWhiteSpace(delivery.BasicProperties.Type))
                throw new InvalidIntegrationEventException("RabbitMQ event type is required.");
            var contractVersion = GetContractVersion(delivery.BasicProperties.Headers);
            if (!IntegrationEventContract.IsSupportedVersion(contractVersion))
                throw new InvalidIntegrationEventException(
                    $"Unsupported integration event contract version '{contractVersion}'.");

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IntegrationEventProcessor>();
            var result = await processor.ProcessAsync(
                messageId,
                delivery.BasicProperties.Type,
                delivery.Body,
                stoppingToken);
            var eventTypeTag = new KeyValuePair<string, object?>(
                "messaging.event.type",
                delivery.BasicProperties.Type);
            if (result == IntegrationEventProcessingResult.AlreadyProcessed)
                WorkerTelemetry.DuplicateEvents.Add(1, eventTypeTag);
            else
                WorkerTelemetry.ConsumedEvents.Add(1, eventTypeTag);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (InvalidIntegrationEventException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            WorkerTelemetry.FailedEvents.Add(
                1,
                new KeyValuePair<string, object?>("failure.kind", "invalid"));
            logger.LogWarning(
                exception,
                "Rejecting invalid integration event {MessageId}.",
                delivery.BasicProperties.MessageId);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            if (channel.IsOpen)
                await channel.BasicNackAsync(delivery.DeliveryTag, false, true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            WorkerTelemetry.FailedEvents.Add(
                1,
                new KeyValuePair<string, object?>("failure.kind", "transient"));
            logger.LogWarning(
                exception,
                "Integration event {MessageId} failed and will be retried.",
                delivery.BasicProperties.MessageId);
            await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), stoppingToken);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    private static string? GetContractVersion(IDictionary<string, object?>? headers)
    {
        if (headers is null ||
            !headers.TryGetValue(IntegrationEventContract.VersionHeaderName, out var value) ||
            value is null)
            return null;

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> bytes => Encoding.UTF8.GetString(bytes.Span),
            string text => text,
            _ => value.ToString()
        };
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            _options.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            _options.DeadLetterExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var deadQueueArguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum"
        };
        await channel.QueueDeclareAsync(
            _options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: deadQueueArguments,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.DeadLetterQueueName,
            _options.DeadLetterExchangeName,
            "#",
            cancellationToken: cancellationToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = _options.DeliveryLimit,
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
            "order.*",
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.QueueName,
            _options.ExchangeName,
            IntegrationEventTypes.PaymentFailed,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.QueueName,
            _options.ExchangeName,
            StockIntegrationEventProcessor.EventType,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.QueueName,
            _options.ExchangeName,
            EmailSentIntegrationEventProcessor.EventType,
            cancellationToken: cancellationToken);
    }
}
