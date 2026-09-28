using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Messaging;
using ECommerce.Worker.Notifications;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Processing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ECommerce.Worker.Tests.Integration;

public sealed class WorkerPostgreSqlIntegrationTests
{
    [WorkerPostgreSqlIntegrationFact]
    public async Task ConcurrentRedelivery_PersistsEachEffectExactlyOnce()
    {
        await using var container = new PostgreSqlBuilder("postgres:18.6-alpine3.23")
            .WithDatabase("ecommerce_worker_redelivery_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseNpgsql(container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WorkerDbContext.SchemaName))
            .Options;
        await using (var migrationContext = new WorkerDbContext(options))
            await migrationContext.Database.MigrateAsync();

        var messageId = Guid.NewGuid();
        var payload = new OrderIntegrationEventPayload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "customer@example.com",
            "Pending",
            49.90m,
            DateTimeOffset.UtcNow.AddMinutes(30),
            null,
            DateTimeOffset.UtcNow,
            [new OrderIntegrationEventItem(Guid.NewGuid(), "Product", 49.90m, 1, 49.90m)]);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);

        var first = Task.Run(async () =>
        {
            await using var context = new WorkerDbContext(options);
            return await new OrderIntegrationEventProcessor(context).ProcessAsync(
                messageId,
                IntegrationEventTypes.OrderCreated,
                body);
        });
        var second = Task.Run(async () =>
        {
            await using var context = new WorkerDbContext(options);
            return await new OrderIntegrationEventProcessor(context).ProcessAsync(
                messageId,
                IntegrationEventTypes.OrderCreated,
                body);
        });

        var results = await Task.WhenAll(first, second);

        results.Should().BeEquivalentTo(
            [
                IntegrationEventProcessingResult.Processed,
                IntegrationEventProcessingResult.AlreadyProcessed
            ]);
        await using var assertionContext = new WorkerDbContext(options);
        (await assertionContext.ConsumedIntegrationEvents.CountAsync()).Should().Be(1);
        (await assertionContext.OrderProjections.CountAsync()).Should().Be(1);
        (await assertionContext.NotificationOutboxMessages.CountAsync()).Should().Be(1);
        (await assertionContext.AnalyticsEvents.CountAsync()).Should().Be(1);
    }

    [WorkerPostgreSqlIntegrationFact]
    public async Task InboxMessageLock_SerializesConcurrentConsumers()
    {
        await using var container = new PostgreSqlBuilder("postgres:18.6-alpine3.23")
            .WithDatabase("ecommerce_worker_lock_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;
        var messageId = Guid.NewGuid();

        await using var firstContext = new WorkerDbContext(options);
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        await firstContext.AcquireInboxMessageLockAsync(messageId);

        var secondAttemptStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondClaim = Task.Run(async () =>
        {
            await using var secondContext = new WorkerDbContext(options);
            await using var secondTransaction =
                await secondContext.Database.BeginTransactionAsync();
            secondAttemptStarted.SetResult();
            await secondContext.AcquireInboxMessageLockAsync(messageId);
            await secondTransaction.CommitAsync();
        });

        await secondAttemptStarted.Task;
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        var secondClaimWasBlocked = !secondClaim.IsCompleted;

        await firstTransaction.CommitAsync();
        await secondClaim;

        secondClaimWasBlocked.Should().BeTrue();
    }

    [WorkerPostgreSqlIntegrationFact]
    public async Task MigrationsAndEventProcessing_PersistAtomicWorkerState()
    {
        await using var container = new PostgreSqlBuilder("postgres:18.6-alpine3.23")
            .WithDatabase("ecommerce_worker_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseNpgsql(container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WorkerDbContext.SchemaName))
            .Options;

        await using var context = new WorkerDbContext(options);
        await context.Database.MigrateAsync();
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
        var processor = new OrderIntegrationEventProcessor(context);

        await processor.ProcessAsync(
            Guid.NewGuid(),
            "order.created",
            JsonSerializer.SerializeToUtf8Bytes(payload));
        await processor.ProcessAsync(
            Guid.NewGuid(),
            "order.paid",
            JsonSerializer.SerializeToUtf8Bytes(payload with
            {
                CustomerEmail = null,
                Status = "Paid",
                OccurredAt = payload.OccurredAt.AddSeconds(1)
            }));
        var stockPayload = new StockUpdatedIntegrationEventPayload(
            payload.Items.First().ProductId,
            9,
            payload.OrderId,
            StockUpdateReasons.Reserved,
            payload.OccurredAt.AddSeconds(2));
        var stockProcessor = new StockIntegrationEventProcessor(context);
        await stockProcessor.ProcessAsync(
            Guid.NewGuid(),
            StockIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(stockPayload));
        var processorOptions = Microsoft.Extensions.Options.Options.Create(
            new NotificationProcessorOptions
            {
                BatchSize = 10,
                LockSeconds = 60,
                MaximumAttempts = 8
            });
        var notificationProcessor = new NotificationOutboxProcessor(
            context,
            new RecordingOrderEmailSender(),
            processorOptions,
            NullLogger<NotificationOutboxProcessor>.Instance);
        (await notificationProcessor.ProcessBatchAsync()).Should().Be(2);
        var eventPublisher = new RecordingIntegrationEventPublisher();
        var eventProcessor = new WorkerIntegrationEventOutboxProcessor(
            context,
            eventPublisher,
            processorOptions,
            NullLogger<WorkerIntegrationEventOutboxProcessor>.Instance);
        (await eventProcessor.ProcessBatchAsync()).Should().Be(2);
        var emailProcessor = new EmailSentIntegrationEventProcessor(context);
        foreach (var integrationEvent in eventPublisher.Events)
        {
            await emailProcessor.ProcessAsync(
                integrationEvent.Id,
                integrationEvent.Type,
                JsonSerializer.SerializeToUtf8Bytes(
                    JsonSerializer.Deserialize<EmailSentIntegrationEventPayload>(
                        integrationEvent.Payload)!),
                CancellationToken.None);
        }

        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(5);
        (await context.NotificationOutboxMessages.CountAsync()).Should().Be(2);
        (await context.NotificationOutboxMessages.CountAsync(message => message.SentAt != null))
            .Should().Be(2);
        (await context.IntegrationOutboxMessages.CountAsync()).Should().Be(2);
        (await context.IntegrationOutboxMessages.CountAsync(message => message.ProcessedAt != null))
            .Should().Be(2);
        eventPublisher.Events.Should().HaveCount(2);
        (await context.EmailDeliveryProjections.CountAsync()).Should().Be(2);
        (await context.Invoices.CountAsync()).Should().Be(1);
        (await context.AnalyticsEvents.CountAsync()).Should().Be(5);
        (await context.InventoryProjections.SingleAsync()).AvailableStock.Should().Be(9);

        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'worker';",
            connection);
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().BeGreaterThanOrEqualTo(8);
    }

    private sealed class RecordingOrderEmailSender : IOrderEmailSender
    {
        public Task SendAsync(
            string recipient,
            string subject,
            string body,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingIntegrationEventPublisher : IWorkerIntegrationEventPublisher
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
}

public sealed class WorkerPostgreSqlIntegrationFactAttribute : FactAttribute
{
    public WorkerPostgreSqlIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set RUN_POSTGRES_INTEGRATION_TESTS=true and start Docker to run this test.";
        }
    }
}
