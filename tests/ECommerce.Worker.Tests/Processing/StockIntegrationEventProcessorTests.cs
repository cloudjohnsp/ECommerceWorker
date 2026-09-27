using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Processing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Worker.Tests.Processing;

public sealed class StockIntegrationEventProcessorTests
{
    [Fact]
    public async Task StockUpdated_PersistsProjectionAndInboxAtomically()
    {
        await using var context = CreateContext();
        var processor = new StockIntegrationEventProcessor(context);
        var messageId = Guid.NewGuid();
        var payload = CreatePayload(7, StockUpdateReasons.Reserved, Guid.NewGuid());

        var result = await processor.ProcessAsync(
            messageId,
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(payload));

        result.Should().Be(IntegrationEventProcessingResult.Processed);
        var projection = await context.InventoryProjections.SingleAsync();
        projection.ProductId.Should().Be(payload.ProductId);
        projection.AvailableStock.Should().Be(7);
        projection.LastOrderId.Should().Be(payload.OrderId);
        projection.LastReason.Should().Be(StockUpdateReasons.Reserved);
        (await context.ConsumedIntegrationEvents.SingleAsync()).MessageId.Should().Be(messageId);
    }

    [Fact]
    public async Task ReplayedMessage_IsIgnoredWithoutChangingProjection()
    {
        await using var context = CreateContext();
        var processor = new StockIntegrationEventProcessor(context);
        var messageId = Guid.NewGuid();
        var payload = CreatePayload(8, StockUpdateReasons.Adjusted);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);

        await processor.ProcessAsync(messageId, StockIntegrationEventProcessor.EventType, body);
        var result = await processor.ProcessAsync(
            messageId,
            StockIntegrationEventProcessor.EventType,
            body);

        result.Should().Be(IntegrationEventProcessingResult.AlreadyProcessed);
        (await context.InventoryProjections.CountAsync()).Should().Be(1);
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task NewerEvent_UpdatesExistingProjection()
    {
        await using var context = CreateContext();
        var processor = new StockIntegrationEventProcessor(context);
        var created = CreatePayload(10, StockUpdateReasons.Created);
        await processor.ProcessAsync(
            Guid.NewGuid(),
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(created));
        var adjusted = created with
        {
            AvailableStock = 15,
            Reason = StockUpdateReasons.Adjusted,
            OccurredAt = created.OccurredAt.AddSeconds(1)
        };

        await processor.ProcessAsync(
            Guid.NewGuid(),
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(adjusted));

        var projection = await context.InventoryProjections.SingleAsync();
        projection.AvailableStock.Should().Be(15);
        projection.LastReason.Should().Be(StockUpdateReasons.Adjusted);
        projection.UpdatedAt.Should().Be(adjusted.OccurredAt);
    }

    [Fact]
    public async Task OlderEvent_IsConsumedWithoutRegressingProjection()
    {
        await using var context = CreateContext();
        var processor = new StockIntegrationEventProcessor(context);
        var current = CreatePayload(5, StockUpdateReasons.Reserved);
        await processor.ProcessAsync(
            Guid.NewGuid(),
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(current));
        var older = current with
        {
            AvailableStock = 10,
            Reason = StockUpdateReasons.Created,
            OccurredAt = current.OccurredAt.AddSeconds(-1)
        };

        await processor.ProcessAsync(
            Guid.NewGuid(),
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(older));

        var projection = await context.InventoryProjections.SingleAsync();
        projection.AvailableStock.Should().Be(5);
        projection.UpdatedAt.Should().Be(current.OccurredAt);
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task UnsupportedReason_IsRejectedWithoutPersistingInboxEntry()
    {
        await using var context = CreateContext();
        var processor = new StockIntegrationEventProcessor(context);
        var payload = CreatePayload(10, "unknown");

        var action = () => processor.ProcessAsync(
            Guid.NewGuid(),
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(payload));

        await action.Should().ThrowAsync<InvalidIntegrationEventException>();
        (await context.InventoryProjections.CountAsync()).Should().Be(0);
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(0);
    }

    private static WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkerDbContext(options);
    }

    private static StockUpdatedIntegrationEventPayload CreatePayload(
        int availableStock,
        string reason,
        Guid? orderId = null) => new(
        Guid.NewGuid(),
        availableStock,
        orderId,
        reason,
        DateTimeOffset.UtcNow);
}
