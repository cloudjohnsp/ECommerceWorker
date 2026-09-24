using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Messaging;
using FluentAssertions;

namespace ECommerce.Worker.Tests;

public sealed class OrderCreatedMessageProcessorTests
{
    [Fact]
    public async Task Process_ValidEvent_HandlesAndMarksMessageProcessed()
    {
        var store = new InMemoryProcessedMessageStore();
        var handler = new RecordingOrderCreatedHandler();
        var processor = new OrderCreatedMessageProcessor(store, handler);
        var messageId = Guid.NewGuid();
        var order = CreateOrder();

        var result = await processor.ProcessAsync(
            messageId,
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(order));

        result.Should().Be(MessageProcessingResult.Completed);
        handler.Orders.Should().ContainSingle().Which.Should().BeEquivalentTo(order);
        store.ProcessedIds.Should().Contain(messageId);
    }

    [Fact]
    public async Task Process_DuplicateEvent_DoesNotExecuteHandlerAgain()
    {
        var messageId = Guid.NewGuid();
        var store = new InMemoryProcessedMessageStore();
        store.ProcessedIds.Add(messageId);
        var handler = new RecordingOrderCreatedHandler();
        var processor = new OrderCreatedMessageProcessor(store, handler);

        var result = await processor.ProcessAsync(
            messageId,
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(CreateOrder()));

        result.Should().Be(MessageProcessingResult.Duplicate);
        handler.Orders.Should().BeEmpty();
    }

    [Theory]
    [InlineData("other.event", "{}")]
    [InlineData("order.created", "not-json")]
    [InlineData("order.created", "{}")]
    public async Task Process_InvalidEvent_DoesNotMarkMessageProcessed(string eventType, string payload)
    {
        var store = new InMemoryProcessedMessageStore();
        var handler = new RecordingOrderCreatedHandler();
        var processor = new OrderCreatedMessageProcessor(store, handler);

        var result = await processor.ProcessAsync(
            Guid.NewGuid(),
            eventType,
            System.Text.Encoding.UTF8.GetBytes(payload));

        result.Should().Be(MessageProcessingResult.Invalid);
        store.ProcessedIds.Should().BeEmpty();
        handler.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_WhenHandlerFails_DoesNotMarkMessageProcessed()
    {
        var store = new InMemoryProcessedMessageStore();
        var processor = new OrderCreatedMessageProcessor(store, new FailingOrderCreatedHandler());

        var action = () => processor.ProcessAsync(
            Guid.NewGuid(),
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(CreateOrder()));

        await action.Should().ThrowAsync<InvalidOperationException>();
        store.ProcessedIds.Should().BeEmpty();
    }

    private static OrderCreatedEvent CreateOrder() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        50m,
        DateTimeOffset.UtcNow,
        [new OrderCreatedItem(Guid.NewGuid(), Guid.NewGuid(), "Product", 25m, 2, 50m)]);

    private sealed class InMemoryProcessedMessageStore : IProcessedMessageStore
    {
        public HashSet<Guid> ProcessedIds { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> WasProcessedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProcessedIds.Contains(messageId));

        public Task MarkProcessedAsync(
            Guid messageId,
            string eventType,
            CancellationToken cancellationToken = default)
        {
            ProcessedIds.Add(messageId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOrderCreatedHandler : IOrderCreatedHandler
    {
        public List<OrderCreatedEvent> Orders { get; } = [];
        public Task HandleAsync(OrderCreatedEvent order, CancellationToken cancellationToken = default)
        {
            Orders.Add(order);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingOrderCreatedHandler : IOrderCreatedHandler
    {
        public Task HandleAsync(OrderCreatedEvent order, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Handler failed.");
    }
}
