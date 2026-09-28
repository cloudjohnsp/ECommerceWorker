using System.Globalization;
using System.Text;
using RabbitMQ.Client;

namespace ECommerce.Worker.Messaging;

internal enum IntegrationEventFailureAction
{
    Retry,
    DeadLetter
}

internal static class IntegrationEventFailureReasons
{
    public const string InvalidEvent = "invalid_event";
    public const string RetryExhausted = "retry_exhausted";
}

internal static class IntegrationEventDeliveryPolicy
{
    public const string RetryCountHeader = "x-retry-count";
    public const string FailureReasonHeader = "x-failure-reason";
    public const string OriginalExchangeHeader = "x-original-exchange";
    public const string OriginalRoutingKeyHeader = "x-original-routing-key";
    public const string DeadLetteredAtHeader = "x-dead-lettered-at";

    public static IntegrationEventFailureAction DecideTransientFailure(
        int retryCount,
        int maximumRetryAttempts) => retryCount < maximumRetryAttempts
            ? IntegrationEventFailureAction.Retry
            : IntegrationEventFailureAction.DeadLetter;

    public static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null ||
            !headers.TryGetValue(RetryCountHeader, out var rawValue) ||
            rawValue is null)
            return 0;

        var value = rawValue switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> bytes => Encoding.UTF8.GetString(bytes.Span),
            _ => Convert.ToString(rawValue, CultureInfo.InvariantCulture)
        };

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
               count >= 0
            ? count
            : 0;
    }

    public static BasicProperties CreateRetryProperties(
        IReadOnlyBasicProperties source,
        int retryCount)
    {
        var properties = Copy(source);
        properties.Expiration = null;
        properties.Headers![RetryCountHeader] = retryCount;
        return properties;
    }

    public static BasicProperties CreateDeadLetterProperties(
        IReadOnlyBasicProperties source,
        int retryCount,
        string failureReason,
        string originalExchange,
        string originalRoutingKey,
        DateTimeOffset deadLetteredAt)
    {
        var properties = Copy(source);
        properties.Expiration = null;
        properties.Headers![RetryCountHeader] = retryCount;
        properties.Headers[FailureReasonHeader] = failureReason;
        properties.Headers[OriginalExchangeHeader] = originalExchange;
        properties.Headers[OriginalRoutingKeyHeader] = originalRoutingKey;
        properties.Headers[DeadLetteredAtHeader] = deadLetteredAt.ToString("O");
        return properties;
    }

    private static BasicProperties Copy(IReadOnlyBasicProperties source) => new()
    {
        AppId = source.AppId,
        ContentEncoding = source.ContentEncoding,
        ContentType = source.ContentType,
        CorrelationId = source.CorrelationId,
        DeliveryMode = DeliveryModes.Persistent,
        Expiration = source.Expiration,
        Headers = source.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source.Headers, StringComparer.Ordinal),
        MessageId = source.MessageId,
        Priority = source.Priority,
        ReplyTo = source.ReplyTo,
        Timestamp = source.Timestamp,
        Type = source.Type,
        UserId = source.UserId
    };
}
