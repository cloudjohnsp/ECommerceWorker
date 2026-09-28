using System.Text;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Messaging;
using ECommerce.Worker.Processing;
using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ECommerce.Worker.Tests.Messaging;

public sealed class RabbitMqWorkerIntegrationEventPublisherContractTests
{
    [Fact]
    public void CreateProperties_UsesVersion1TransportContract()
    {
        var integrationEvent = new WorkerIntegrationEvent(
            Guid.NewGuid(),
            IntegrationEventTypes.EmailSent,
            "{}",
            DateTimeOffset.Parse("2026-09-27T12:00:00+00:00"),
            "checkout-123");

        var properties = RabbitMqWorkerIntegrationEventPublisher.CreateProperties(integrationEvent);

        properties.ContentType.Should().Be("application/json");
        properties.MessageId.Should().Be(integrationEvent.Id.ToString());
        properties.Type.Should().Be(integrationEvent.Type);
        properties.Timestamp.UnixTime.Should().Be(integrationEvent.OccurredAt.ToUnixTimeSeconds());
        properties.Headers.Should().ContainKey(IntegrationEventContract.VersionHeaderName);
        Encoding.UTF8.GetString((byte[])properties.Headers![IntegrationEventContract.VersionHeaderName]!)
            .Should().Be(IntegrationEventContract.Version1);
        Encoding.UTF8.GetString((byte[])properties.Headers![IntegrationEventContract.CorrelationHeaderName]!)
            .Should().Be("checkout-123");

        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(
            RabbitMqWorkerIntegrationEventPublisher.CreateBody(integrationEvent));
        envelope.Should().NotBeNull();
        envelope!.MessageId.Should().Be(integrationEvent.Id);
        envelope.EventType.Should().Be(integrationEvent.Type);
        envelope.Version.Should().Be(IntegrationEventContract.Version1);
        envelope.OccurredAt.Should().Be(integrationEvent.OccurredAt);
        envelope.CorrelationId.Should().Be("checkout-123");
        envelope.Payload.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void SerializedEnvelope_MatchesCanonicalVersion1Example()
    {
        var root = FindSolutionRoot();
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root,
            "docs",
            "contracts",
            "v1",
            "order-paid.example.json")))!;
        var integrationEvent = new WorkerIntegrationEvent(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "order.paid",
            expected["payload"]!.ToJsonString(),
            DateTimeOffset.Parse("2026-09-27T12:00:00+00:00"),
            "checkout-123");

        var actual = JsonNode.Parse(
            RabbitMqWorkerIntegrationEventPublisher.CreateBody(integrationEvent));

        JsonNode.DeepEquals(actual, expected).Should().BeTrue();
    }

    [Fact]
    public void Consumer_ExtractsAndValidatesVersionedEnvelope()
    {
        var integrationEvent = new WorkerIntegrationEvent(
            Guid.NewGuid(),
            IntegrationEventTypes.OrderPaid,
            "{\"OrderId\":\"11111111-1111-1111-1111-111111111111\"}",
            DateTimeOffset.Parse("2026-09-27T12:00:00+00:00"),
            "checkout-123");
        var body = RabbitMqWorkerIntegrationEventPublisher.CreateBody(integrationEvent);

        var parsed = RabbitMqOrderConsumer.ParseDelivery(
            integrationEvent.Id,
            integrationEvent.Type,
            integrationEvent.Version,
            null,
            body);

        parsed.MessageId.Should().Be(integrationEvent.Id);
        parsed.EventType.Should().Be(integrationEvent.Type);
        parsed.Version.Should().Be("1");
        parsed.CorrelationId.Should().Be("checkout-123");
        Encoding.UTF8.GetString(parsed.Payload.Span).Should().Contain("OrderId");
    }

    [Fact]
    public void Consumer_RejectsEnvelopeThatDisagreesWithTransportMetadata()
    {
        var integrationEvent = new WorkerIntegrationEvent(
            Guid.NewGuid(),
            IntegrationEventTypes.OrderPaid,
            "{}",
            DateTimeOffset.UtcNow,
            "checkout-123");

        var action = () => RabbitMqOrderConsumer.ParseDelivery(
            Guid.NewGuid(),
            integrationEvent.Type,
            integrationEvent.Version,
            null,
            RabbitMqWorkerIntegrationEventPublisher.CreateBody(integrationEvent));

        action.Should().Throw<InvalidIntegrationEventException>()
            .WithMessage("*does not match AMQP metadata*");
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ECommerceWorker.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Worker solution root.");
    }
}
