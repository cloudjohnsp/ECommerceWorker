using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Processing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Worker.Tests.Processing;

public sealed class IntegrationEventProcessorTests
{
    [Fact]
    public async Task StockUpdated_IsDispatchedToStockProcessor()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);
        var payload = new StockUpdatedIntegrationEventPayload(
            Guid.NewGuid(),
            12,
            null,
            StockUpdateReasons.Created,
            DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(
            Guid.NewGuid(),
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(payload));

        result.Should().Be(IntegrationEventProcessingResult.Processed);
        (await context.InventoryProjections.SingleAsync()).ProductId.Should().Be(payload.ProductId);
        (await context.OrderProjections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task OrderCreated_IsDispatchedToOrderProcessor()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);
        var payload = new OrderIntegrationEventPayload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "customer@example.com",
            "Pending",
            99.90m,
            DateTimeOffset.UtcNow.AddMinutes(30),
            null,
            DateTimeOffset.UtcNow,
            [new OrderIntegrationEventItem(Guid.NewGuid(), "Product", 99.90m, 1, 99.90m)]);

        var result = await processor.ProcessAsync(
            Guid.NewGuid(),
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(payload),
            correlationId: "checkout-123");

        result.Should().Be(IntegrationEventProcessingResult.Processed);
        (await context.OrderProjections.SingleAsync()).OrderId.Should().Be(payload.OrderId);
        (await context.InventoryProjections.CountAsync()).Should().Be(0);
        (await context.NotificationOutboxMessages.SingleAsync()).CorrelationId
            .Should().Be("checkout-123");
    }

    [Fact]
    public async Task EmailSent_IsDispatchedToEmailProcessor()
    {
        await using var context = CreateContext();
        var processor = CreateProcessor(context);
        var payload = new EmailSentIntegrationEventPayload(
            Guid.NewGuid(),
            EmailDeliveryCategories.EmailConfirmation,
            DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(
            Guid.NewGuid(),
            EmailSentIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(payload));

        result.Should().Be(IntegrationEventProcessingResult.Processed);
        (await context.EmailDeliveryProjections.SingleAsync()).DeliveryId
            .Should().Be(payload.DeliveryId);
        (await context.OrderProjections.CountAsync()).Should().Be(0);
    }

    private static IntegrationEventProcessor CreateProcessor(WorkerDbContext context) => new(
        new OrderIntegrationEventProcessor(context),
        new StockIntegrationEventProcessor(context),
        new EmailSentIntegrationEventProcessor(context));

    private static WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkerDbContext(options);
    }
}
