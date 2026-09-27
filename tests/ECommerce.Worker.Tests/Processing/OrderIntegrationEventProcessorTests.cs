using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Processing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Worker.Tests.Processing;

public sealed class OrderIntegrationEventProcessorTests
{
    [Fact]
    public async Task OrderCreated_PersistsProjectionInboxAndNotificationAtomically()
    {
        await using var context = CreateContext();
        var processor = new OrderIntegrationEventProcessor(context);
        var messageId = Guid.NewGuid();
        var payload = CreatePayload("Pending", "customer@example.com");

        var result = await processor.ProcessAsync(
            messageId,
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(payload));

        result.Should().Be(IntegrationEventProcessingResult.Processed);
        var projection = await context.OrderProjections.SingleAsync();
        projection.OrderId.Should().Be(payload.OrderId);
        projection.CustomerEmail.Should().Be("customer@example.com");
        (await context.ConsumedIntegrationEvents.SingleAsync()).MessageId.Should().Be(messageId);
        (await context.NotificationOutboxMessages.SingleAsync()).SourceMessageId.Should().Be(messageId);
    }

    [Fact]
    public async Task ReplayedMessage_IsIgnoredWithoutDuplicatingSideEffects()
    {
        await using var context = CreateContext();
        var processor = new OrderIntegrationEventProcessor(context);
        var messageId = Guid.NewGuid();
        var body = JsonSerializer.SerializeToUtf8Bytes(CreatePayload("Pending", "customer@example.com"));

        await processor.ProcessAsync(messageId, "order.created", body);
        var result = await processor.ProcessAsync(messageId, "order.created", body);

        result.Should().Be(IntegrationEventProcessingResult.AlreadyProcessed);
        (await context.OrderProjections.CountAsync()).Should().Be(1);
        (await context.NotificationOutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OrderPaid_AfterOrderCreated_GeneratesOneInvoice()
    {
        await using var context = CreateContext();
        var processor = new OrderIntegrationEventProcessor(context);
        var created = CreatePayload("Pending", "customer@example.com");
        await processor.ProcessAsync(
            Guid.NewGuid(),
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(created));
        var paid = created with
        {
            CustomerEmail = null,
            Status = "Paid",
            OccurredAt = created.OccurredAt.AddMinutes(1)
        };

        await processor.ProcessAsync(
            Guid.NewGuid(),
            "order.paid",
            JsonSerializer.SerializeToUtf8Bytes(paid));

        var invoice = await context.Invoices.SingleAsync();
        invoice.OrderId.Should().Be(created.OrderId);
        invoice.Amount.Should().Be(created.Total);
        (await context.NotificationOutboxMessages.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task OrderUpdated_AfterOrderCreated_RefreshesProjectionAndQueuesNotification()
    {
        await using var context = CreateContext();
        var processor = new OrderIntegrationEventProcessor(context);
        var created = CreatePayload("Pending", "customer@example.com");
        await processor.ProcessAsync(
            Guid.NewGuid(),
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(created));
        var updated = created with
        {
            CustomerEmail = null,
            Total = 299.80m,
            OccurredAt = created.OccurredAt.AddSeconds(1)
        };

        await processor.ProcessAsync(
            Guid.NewGuid(),
            "order.updated",
            JsonSerializer.SerializeToUtf8Bytes(updated));

        (await context.OrderProjections.SingleAsync()).Total.Should().Be(299.80m);
        (await context.NotificationOutboxMessages.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task LifecycleEventBeforeOrderCreated_IsRetryable()
    {
        await using var context = CreateContext();
        var processor = new OrderIntegrationEventProcessor(context);
        var payload = CreatePayload("Paid", null);

        var action = () => processor.ProcessAsync(
            Guid.NewGuid(),
            "order.paid",
            JsonSerializer.SerializeToUtf8Bytes(payload));

        await action.Should().ThrowAsync<MissingOrderProjectionException>();
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvalidPayload_IsRejectedWithoutPersistingInboxEntry()
    {
        await using var context = CreateContext();
        var processor = new OrderIntegrationEventProcessor(context);

        var action = () => processor.ProcessAsync(
            Guid.NewGuid(),
            "order.created",
            "not-json"u8.ToArray());

        await action.Should().ThrowAsync<InvalidIntegrationEventException>();
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(0);
    }

    private static WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkerDbContext(options);
    }

    private static OrderIntegrationEventPayload CreatePayload(string status, string? customerEmail) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        customerEmail,
        status,
        149.90m,
        DateTimeOffset.UtcNow,
        [new OrderIntegrationEventItem(Guid.NewGuid(), "Product", 149.90m, 1, 149.90m)]);
}
