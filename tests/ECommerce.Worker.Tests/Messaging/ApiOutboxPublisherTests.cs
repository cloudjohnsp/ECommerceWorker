using ECommerce.Worker.Contracts;
using ECommerce.Worker.Messaging;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Tests.Messaging;

public sealed class ApiOutboxPublisherTests
{
    [Fact]
    public async Task ProcessBatch_PublishesSupportedMessagesAndMarksThemProcessed()
    {
        await using var context = CreateContext();
        var message = new ApiOutboxMessage(Guid.NewGuid(), 1, "{}", DateTimeOffset.UtcNow);
        context.ApiOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(context, publisher);

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        publisher.Events.Should().ContainSingle().Which.Type.Should().Be("order.created");
        message.Status.Should().Be(ApiOutboxMessage.ProcessedStatus);
        message.ProcessedAt.Should().NotBeNull();
        message.Attempts.Should().Be(1);
        message.LockId.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatch_WhenPublishFails_ReleasesLeaseAndSchedulesRetry()
    {
        await using var context = CreateContext();
        var message = new ApiOutboxMessage(Guid.NewGuid(), 11, "{}", DateTimeOffset.UtcNow);
        context.ApiOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var processor = CreateProcessor(context, new ThrowingPublisher());

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        message.Status.Should().Be(ApiOutboxMessage.PendingStatus);
        message.Attempts.Should().Be(1);
        message.LastError.Should().Be("publication_failed:InvalidOperationException");
        message.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow);
        message.LockId.Should().BeNull();
        message.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatch_IgnoresApiLocalOutboxMessageTypes()
    {
        await using var context = CreateContext();
        context.ApiOutboxMessages.Add(
            new ApiOutboxMessage(Guid.NewGuid(), 4, "{}", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(context, publisher);

        (await processor.ProcessBatchAsync()).Should().Be(0);
        publisher.Events.Should().BeEmpty();
    }

    private static WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkerDbContext(options);
    }

    private static ApiOutboxPublisher CreateProcessor(
        WorkerDbContext context,
        IWorkerIntegrationEventPublisher publisher) =>
        new(
            context,
            publisher,
            Microsoft.Extensions.Options.Options.Create(new ApiOutboxPublisherOptions
            {
                BatchSize = 10,
                LockSeconds = 60,
                MaximumAttempts = 8,
                BaseRetryDelaySeconds = 5
            }),
            NullLogger<ApiOutboxPublisher>.Instance);

    private sealed class RecordingPublisher : IWorkerIntegrationEventPublisher
    {
        public List<WorkerIntegrationEvent> Events { get; } = [];

        public Task PublishAsync(
            WorkerIntegrationEvent integrationEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPublisher : IWorkerIntegrationEventPublisher
    {
        public Task PublishAsync(
            WorkerIntegrationEvent integrationEvent,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("RabbitMQ unavailable.");
    }
}
