using ECommerce.Worker.Messaging;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECommerce.Worker.Tests.Messaging;

public sealed class WorkerIntegrationEventOutboxProcessorTests
{
    [Fact]
    public async Task ProcessBatch_WhenPublicationSucceeds_MarksEventProcessed()
    {
        await using var context = CreateContext();
        var message = CreateMessage();
        context.IntegrationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(context, publisher);

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        message.ProcessedAt.Should().NotBeNull();
        message.Attempts.Should().Be(1);
        publisher.Events.Should().ContainSingle().Which.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task ProcessBatch_WhenPublicationFails_SchedulesExponentialRetry()
    {
        await using var context = CreateContext();
        var message = CreateMessage();
        context.IntegrationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var processor = CreateProcessor(context, new FailingPublisher());
        var before = DateTimeOffset.UtcNow;

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        message.ProcessedAt.Should().BeNull();
        message.Attempts.Should().Be(1);
        message.LastError.Should().Be("RabbitMQ unavailable");
        message.NextAttemptAt.Should().BeAfter(before.AddSeconds(9));
        message.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatch_DoesNotClaimEventAtMaximumAttempts()
    {
        await using var context = CreateContext();
        var message = CreateMessage();
        message.MarkFailed("first", DateTimeOffset.UtcNow.AddMinutes(-1));
        message.MarkFailed("second", DateTimeOffset.UtcNow.AddMinutes(-1));
        context.IntegrationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(context, publisher, maximumAttempts: 2);

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(0);
        publisher.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessBatch_DoesNotRepublishProcessedEvent()
    {
        await using var context = CreateContext();
        var message = CreateMessage();
        message.MarkProcessed(DateTimeOffset.UtcNow);
        context.IntegrationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(context, publisher);

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(0);
        publisher.Events.Should().BeEmpty();
    }

    private static WorkerIntegrationEventOutboxProcessor CreateProcessor(
        WorkerDbContext context,
        IWorkerIntegrationEventPublisher publisher,
        int maximumAttempts = 8) => new(
        context,
        publisher,
        Microsoft.Extensions.Options.Options.Create(new NotificationProcessorOptions
        {
            BatchSize = 10,
            LockSeconds = 60,
            MaximumAttempts = maximumAttempts
        }),
        NullLogger<WorkerIntegrationEventOutboxProcessor>.Instance);

    private static WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkerDbContext(options);
    }

    private static WorkerIntegrationOutboxMessage CreateMessage() => new(
        Guid.NewGuid(),
        "email.sent",
        "{\"deliveryId\":\"123\"}",
        DateTimeOffset.UtcNow.AddSeconds(-1));

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

    private sealed class FailingPublisher : IWorkerIntegrationEventPublisher
    {
        public Task PublishAsync(
            WorkerIntegrationEvent integrationEvent,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("RabbitMQ unavailable");
    }
}
