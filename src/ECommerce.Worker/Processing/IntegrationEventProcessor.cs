namespace ECommerce.Worker.Processing;

public sealed class IntegrationEventProcessor(
    OrderIntegrationEventProcessor orderProcessor,
    StockIntegrationEventProcessor stockProcessor,
    EmailSentIntegrationEventProcessor emailProcessor)
{
    public Task<IntegrationEventProcessingResult> ProcessAsync(
        Guid messageId,
        string eventType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default) =>
        eventType switch
        {
            StockIntegrationEventProcessor.EventType =>
                stockProcessor.ProcessAsync(messageId, eventType, body, cancellationToken),
            EmailSentIntegrationEventProcessor.EventType =>
                emailProcessor.ProcessAsync(messageId, eventType, body, cancellationToken),
            _ => orderProcessor.ProcessAsync(messageId, eventType, body, cancellationToken)
        };
}
