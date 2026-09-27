using System.Text;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Messaging;
using FluentAssertions;

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
            DateTimeOffset.Parse("2026-09-27T12:00:00+00:00"));

        var properties = RabbitMqWorkerIntegrationEventPublisher.CreateProperties(integrationEvent);

        properties.ContentType.Should().Be("application/json");
        properties.MessageId.Should().Be(integrationEvent.Id.ToString());
        properties.Type.Should().Be(integrationEvent.Type);
        properties.Timestamp.UnixTime.Should().Be(integrationEvent.OccurredAt.ToUnixTimeSeconds());
        properties.Headers.Should().ContainKey(IntegrationEventContract.VersionHeaderName);
        Encoding.UTF8.GetString((byte[])properties.Headers![IntegrationEventContract.VersionHeaderName]!)
            .Should().Be(IntegrationEventContract.Version1);
    }
}
