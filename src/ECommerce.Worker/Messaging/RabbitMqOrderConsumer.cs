using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Observability;
using ECommerce.Worker.Options;
using ECommerce.Worker.Processing;
using ECommerce.Worker.Resilience;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ECommerce.Worker.Messaging;

public sealed class RabbitMqOrderConsumer(
    ConnectionFactory connectionFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerRabbitMqOptions> options,
    IOptions<WorkerRuntimeOptions> runtimeOptions,
    WorkerOperationDrain drain,
    ILogger<RabbitMqOrderConsumer> logger) : BackgroundService
{
    private readonly WorkerRabbitMqOptions _options = options.Value;
    private readonly WorkerRuntimeOptions _runtimeOptions = runtimeOptions.Value;

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
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(
            channelOptions,
            stoppingToken);
        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(
            0,
            (ushort)_runtimeOptions.ConsumerConcurrency,
            false,
            stoppingToken);

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
        var consumerTag = await channel.BasicConsumeAsync(
            _options.QueueName,
            autoAck: false,
            consumer,
            stoppingToken);

        logger.LogInformation("Consuming RabbitMQ queue {QueueName}.", _options.QueueName);
        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        await Task.WhenAny(disconnected.Task, cancelled);
        if (stoppingToken.IsCancellationRequested)
        {
            drain.StopAccepting();
            if (channel.IsOpen)
                await channel.BasicCancelAsync(consumerTag, false, CancellationToken.None);
            if (!await drain.WaitForDrainAsync(CancellationToken.None))
                logger.LogWarning("Worker drain deadline elapsed with operations still in progress.");
            return;
        }
        throw new InvalidOperationException("RabbitMQ connection was closed.");
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        CancellationToken stoppingToken)
    {
        if (!drain.TryBegin(out var lease))
        {
            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: true,
                CancellationToken.None);
            return;
        }
        using (lease)
        {
            await HandleDeliveryCoreAsync(channel, delivery, lease.CancellationToken);
        }
    }

    private async Task HandleDeliveryCoreAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        CancellationToken operationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = WorkerTelemetry.ActivitySource.StartActivity(
            "rabbitmq.consume",
            ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", _options.QueueName);
        activity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);
        activity?.SetTag("messaging.event.type", delivery.BasicProperties.Type);
        var diagnosticCorrelationId = NormalizeCorrelationIdForDiagnostics(
            GetHeader(delivery.BasicProperties.Headers, IntegrationEventContract.CorrelationHeaderName),
            delivery.BasicProperties.MessageId);
        activity?.SetTag("correlation.id", diagnosticCorrelationId);
        using var transportLogScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = diagnosticCorrelationId,
            ["MessageId"] = delivery.BasicProperties.MessageId ?? string.Empty,
            ["EventType"] = delivery.BasicProperties.Type ?? string.Empty
        });

        try
        {
            if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
                throw new InvalidIntegrationEventException("RabbitMQ MessageId must be a GUID.");
            if (string.IsNullOrWhiteSpace(delivery.BasicProperties.Type))
                throw new InvalidIntegrationEventException("RabbitMQ event type is required.");
            var contractVersion = GetHeader(
                delivery.BasicProperties.Headers,
                IntegrationEventContract.VersionHeaderName);
            if (!IntegrationEventContract.IsSupportedVersion(contractVersion))
                throw new InvalidIntegrationEventException(
                    $"Unsupported integration event contract version '{contractVersion}'.");

            var parsed = ParseDelivery(
                messageId,
                delivery.BasicProperties.Type,
                contractVersion,
                delivery.BasicProperties.Headers,
                delivery.Body);
            var parsedCorrelationId = NormalizeCorrelationIdForDiagnostics(
                parsed.CorrelationId,
                parsed.MessageId.ToString("N"));
            activity?.SetTag("correlation.id", parsedCorrelationId);
            activity?.SetTag("messaging.contract.version", parsed.Version);

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IntegrationEventProcessor>();
            using var logScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = parsedCorrelationId,
                ["MessageId"] = parsed.MessageId,
                ["EventType"] = parsed.EventType
            });
            var result = await processor.ProcessAsync(
                parsed.MessageId,
                parsed.EventType,
                parsed.Payload,
                operationToken,
                parsed.CorrelationId);
            var eventTypeTag = new KeyValuePair<string, object?>(
                "messaging.event.type",
                delivery.BasicProperties.Type);
            if (result == IntegrationEventProcessingResult.AlreadyProcessed)
                WorkerTelemetry.DuplicateEvents.Add(1, eventTypeTag);
            else
                WorkerTelemetry.ConsumedEvents.Add(1, eventTypeTag);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, operationToken);
        }
        catch (InvalidIntegrationEventException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            WorkerTelemetry.FailedEvents.Add(
                1,
                new KeyValuePair<string, object?>("failure.kind", "invalid"));
            logger.LogWarning(
                "Rejecting invalid integration event {MessageId}.",
                delivery.BasicProperties.MessageId);
            await DeadLetterOrRequeueAsync(
                channel,
                delivery,
                IntegrationEventFailureReasons.InvalidEvent,
                operationToken);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
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
            var retryCount = IntegrationEventDeliveryPolicy.GetRetryCount(
                delivery.BasicProperties.Headers);
            if (IntegrationEventDeliveryPolicy.DecideTransientFailure(
                    retryCount,
                    _options.MaximumRetryAttempts) == IntegrationEventFailureAction.Retry)
            {
                logger.LogWarning(
                    exception,
                    "Integration event {MessageId} failed; scheduling retry {RetryAttempt}/{MaximumRetryAttempts}.",
                    delivery.BasicProperties.MessageId,
                    retryCount + 1,
                    _options.MaximumRetryAttempts);
                await RetryOrRequeueAsync(channel, delivery, retryCount + 1, operationToken);
            }
            else
            {
                logger.LogError(
                    exception,
                    "Integration event {MessageId} exhausted {MaximumRetryAttempts} retries and will be dead-lettered.",
                    delivery.BasicProperties.MessageId,
                    _options.MaximumRetryAttempts);
                await DeadLetterOrRequeueAsync(
                    channel,
                    delivery,
                    IntegrationEventFailureReasons.RetryExhausted,
                    operationToken);
            }
        }
        WorkerTelemetry.OperationDuration.Record(
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
            new KeyValuePair<string, object?>("operation", "rabbitmq.consume"),
            new KeyValuePair<string, object?>("messaging.event.type", delivery.BasicProperties.Type));
    }

    private async Task RetryOrRequeueAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        int retryCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var properties = IntegrationEventDeliveryPolicy.CreateRetryProperties(
                delivery.BasicProperties,
                retryCount);
            await channel.BasicPublishAsync(
                _options.RetryExchangeName,
                delivery.RoutingKey,
                mandatory: true,
                properties,
                delivery.Body,
                cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            WorkerTelemetry.RetriedEvents.Add(
                1,
                new KeyValuePair<string, object?>("messaging.event.type", delivery.BasicProperties.Type),
                new KeyValuePair<string, object?>("retry.attempt", retryCount));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Could not schedule retry for integration event {MessageId}; requeueing the original delivery.",
                delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
        }
    }

    private async Task DeadLetterOrRequeueAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        string failureReason,
        CancellationToken cancellationToken)
    {
        try
        {
            var retryCount = IntegrationEventDeliveryPolicy.GetRetryCount(
                delivery.BasicProperties.Headers);
            var properties = IntegrationEventDeliveryPolicy.CreateDeadLetterProperties(
                delivery.BasicProperties,
                retryCount,
                failureReason,
                delivery.Exchange,
                delivery.RoutingKey,
                DateTimeOffset.UtcNow);
            await channel.BasicPublishAsync(
                _options.DeadLetterExchangeName,
                delivery.RoutingKey,
                mandatory: true,
                properties,
                delivery.Body,
                cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            WorkerTelemetry.DeadLetteredEvents.Add(
                1,
                new KeyValuePair<string, object?>("messaging.event.type", delivery.BasicProperties.Type),
                new KeyValuePair<string, object?>("failure.reason", failureReason));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Could not dead-letter integration event {MessageId}; requeueing the original delivery.",
                delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
        }
    }

    internal static ParsedIntegrationEvent ParseDelivery(
        Guid transportMessageId,
        string transportEventType,
        string? transportVersion,
        IDictionary<string, object?>? headers,
        ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("messageId", out _))
            {
                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(body.Span)
                    ?? throw new InvalidIntegrationEventException("Integration event envelope is empty.");
                if (envelope.MessageId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(envelope.EventType) ||
                    string.IsNullOrWhiteSpace(envelope.Version) ||
                    !IntegrationEventContract.IsSupportedVersion(envelope.Version) ||
                    string.IsNullOrWhiteSpace(envelope.CorrelationId) ||
                    envelope.CorrelationId.Length > 128 ||
                    envelope.OccurredAt == default ||
                    envelope.Payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    throw new InvalidIntegrationEventException("Integration event envelope is invalid.");
                }
                if (envelope.MessageId != transportMessageId ||
                    !string.Equals(envelope.EventType, transportEventType, StringComparison.Ordinal) ||
                    !string.IsNullOrWhiteSpace(transportVersion) &&
                    !string.Equals(envelope.Version, transportVersion, StringComparison.Ordinal))
                {
                    throw new InvalidIntegrationEventException(
                        "Integration event envelope does not match AMQP metadata.");
                }

                return new ParsedIntegrationEvent(
                    envelope.MessageId,
                    envelope.EventType,
                    envelope.Version,
                    envelope.OccurredAt,
                    envelope.CorrelationId,
                    Encoding.UTF8.GetBytes(envelope.Payload.GetRawText()));
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidIntegrationEventException(
                "Integration event envelope is not valid JSON.",
                exception);
        }

        var legacyCorrelationId = GetHeader(
            headers,
            IntegrationEventContract.CorrelationHeaderName);
        return new ParsedIntegrationEvent(
            transportMessageId,
            transportEventType,
            IntegrationEventContract.Version1,
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(legacyCorrelationId)
                ? transportMessageId.ToString("N")
                : legacyCorrelationId,
            body);
    }

    private static string? GetHeader(IDictionary<string, object?>? headers, string name)
    {
        if (headers is null ||
            !headers.TryGetValue(name, out var value) ||
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

    private static string NormalizeCorrelationIdForDiagnostics(
        string? correlationId,
        string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(correlationId) &&
            correlationId.Length <= 128 &&
            correlationId.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
            return correlationId;

        return string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback;
    }

    internal sealed record ParsedIntegrationEvent(
        Guid MessageId,
        string EventType,
        string Version,
        DateTimeOffset OccurredAt,
        string CorrelationId,
        ReadOnlyMemory<byte> Payload);

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
        await channel.ExchangeDeclareAsync(
            _options.RetryExchangeName,
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

        var retryQueueArguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-message-ttl"] = checked(_options.RetryDelaySeconds * 1000),
            ["x-dead-letter-exchange"] = _options.ExchangeName
        };
        await channel.QueueDeclareAsync(
            _options.RetryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: retryQueueArguments,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            _options.RetryQueueName,
            _options.RetryExchangeName,
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
