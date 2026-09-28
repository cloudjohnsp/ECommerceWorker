using System.Text;
using ECommerce.Worker.Messaging;
using FluentAssertions;
using RabbitMQ.Client;

namespace ECommerce.Worker.Tests.Messaging;

public sealed class IntegrationEventDeliveryPolicyTests
{
    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(2, 3, true)]
    [InlineData(3, 3, false)]
    public void DecideTransientFailure_StopsAtConfiguredRetryLimit(
        int retryCount,
        int maximumRetryAttempts,
        bool shouldRetry)
    {
        var action = IntegrationEventDeliveryPolicy.DecideTransientFailure(
            retryCount,
            maximumRetryAttempts);

        (action == IntegrationEventFailureAction.Retry).Should().Be(shouldRetry);
    }

    [Fact]
    public void CreateRetryProperties_PreservesMessageIdAndIncrementsAttempt()
    {
        var messageId = Guid.NewGuid();
        var source = CreateProperties(messageId);

        var properties = IntegrationEventDeliveryPolicy.CreateRetryProperties(source, 2);

        properties.MessageId.Should().Be(messageId.ToString());
        properties.Type.Should().Be("order.paid");
        IntegrationEventDeliveryPolicy.GetRetryCount(properties.Headers).Should().Be(2);
        Encoding.UTF8.GetString((byte[])properties.Headers!["x-correlation-id"]!)
            .Should().Be("checkout-123");
    }

    [Fact]
    public void CreateDeadLetterProperties_PreservesReplayIdentityAndUsesSanitizedMetadata()
    {
        var messageId = Guid.NewGuid();
        var source = CreateProperties(messageId);
        var deadLetteredAt = DateTimeOffset.Parse("2026-09-28T01:00:00+00:00");

        var properties = IntegrationEventDeliveryPolicy.CreateDeadLetterProperties(
            source,
            retryCount: 3,
            IntegrationEventFailureReasons.RetryExhausted,
            "ecommerce.events",
            "order.paid",
            deadLetteredAt);

        properties.MessageId.Should().Be(messageId.ToString());
        properties.Headers![IntegrationEventDeliveryPolicy.FailureReasonHeader]
            .Should().Be(IntegrationEventFailureReasons.RetryExhausted);
        properties.Headers[IntegrationEventDeliveryPolicy.OriginalExchangeHeader]
            .Should().Be("ecommerce.events");
        properties.Headers[IntegrationEventDeliveryPolicy.OriginalRoutingKeyHeader]
            .Should().Be("order.paid");
        properties.Headers[IntegrationEventDeliveryPolicy.DeadLetteredAtHeader]
            .Should().Be(deadLetteredAt.ToString("O"));
        IntegrationEventDeliveryPolicy.GetRetryCount(properties.Headers).Should().Be(3);
        properties.Headers.Values
            .Select(value => value?.ToString() ?? string.Empty)
            .Should().NotContain(value =>
                value.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetRetryCount_WhenHeaderIsMalformed_ReturnsZero()
    {
        var headers = new Dictionary<string, object?>
        {
            [IntegrationEventDeliveryPolicy.RetryCountHeader] = "not-a-number"
        };

        IntegrationEventDeliveryPolicy.GetRetryCount(headers).Should().Be(0);
    }

    private static BasicProperties CreateProperties(Guid messageId) => new()
    {
        ContentType = "application/json",
        DeliveryMode = DeliveryModes.Persistent,
        MessageId = messageId.ToString(),
        Type = "order.paid",
        Timestamp = new AmqpTimestamp(1_798_415_200),
        Headers = new Dictionary<string, object?>
        {
            ["x-contract-version"] = Encoding.UTF8.GetBytes("1"),
            ["x-correlation-id"] = Encoding.UTF8.GetBytes("checkout-123")
        }
    };
}
