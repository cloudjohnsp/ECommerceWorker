using System.Text;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text.Json;

namespace ECommerce.Worker.Messaging;

public sealed class RabbitMqWorkerIntegrationEventPublisher(
    ConnectionFactory connectionFactory,
    IOptions<WorkerRabbitMqOptions> options)
    : IWorkerIntegrationEventPublisher, IAsyncDisposable
{
    private readonly WorkerRabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;

    public async Task PublishAsync(
        WorkerIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);
        await channel.ExchangeDeclareAsync(
            _options.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var properties = CreateProperties(integrationEvent);
        await channel.BasicPublishAsync(
            _options.ExchangeName,
            integrationEvent.Type,
            mandatory: true,
            properties,
            CreateBody(integrationEvent),
            cancellationToken);
    }

    internal static byte[] CreateBody(WorkerIntegrationEvent integrationEvent)
    {
        using var payload = JsonDocument.Parse(integrationEvent.Payload);
        var correlationId = NormalizeCorrelationId(integrationEvent);
        var envelope = new IntegrationEventEnvelope(
            integrationEvent.Id,
            integrationEvent.Type,
            integrationEvent.Version,
            integrationEvent.OccurredAt,
            correlationId,
            payload.RootElement.Clone());
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    internal static BasicProperties CreateProperties(WorkerIntegrationEvent integrationEvent) => new()
    {
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent,
        MessageId = integrationEvent.Id.ToString(),
        Type = integrationEvent.Type,
        Timestamp = new AmqpTimestamp(integrationEvent.OccurredAt.ToUnixTimeSeconds()),
        Headers = new Dictionary<string, object?>
        {
            [IntegrationEventContract.VersionHeaderName] =
                Encoding.UTF8.GetBytes(integrationEvent.Version),
            [IntegrationEventContract.CorrelationHeaderName] =
                Encoding.UTF8.GetBytes(NormalizeCorrelationId(integrationEvent))
        }
    };

    private static string NormalizeCorrelationId(WorkerIntegrationEvent integrationEvent) =>
        string.IsNullOrWhiteSpace(integrationEvent.CorrelationId)
            ? integrationEvent.Id.ToString("N")
            : integrationEvent.CorrelationId.Trim();

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _connectionLock.Dispose();
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;
            if (_connection is not null)
                await _connection.DisposeAsync();
            _connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }
}
