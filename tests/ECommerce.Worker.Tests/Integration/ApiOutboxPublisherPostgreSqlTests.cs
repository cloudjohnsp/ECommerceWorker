using ECommerce.Worker.Contracts;
using ECommerce.Worker.Messaging;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ECommerce.Worker.Tests.Integration;

public sealed class ApiOutboxPublisherPostgreSqlTests
{
    [WorkerPostgreSqlIntegrationFact]
    public async Task ConcurrentPublishers_ClaimOneMessageOnlyOnce()
    {
        await using var container = await StartDatabaseAsync();
        var messageId = await CreateApiOutboxTableAndMessageAsync(container.GetConnectionString());
        var publisher = new CountingPublisher();
        await using var firstContext = CreateContext(container.GetConnectionString());
        await using var secondContext = CreateContext(container.GetConnectionString());
        var first = CreateProcessor(firstContext, publisher);
        var second = CreateProcessor(secondContext, publisher);

        var results = await Task.WhenAll(
            first.ProcessBatchAsync(),
            second.ProcessBatchAsync());

        results.Sum().Should().Be(1);
        publisher.Count.Should().Be(1);
        await using var assertionContext = CreateContext(container.GetConnectionString());
        var persisted = await assertionContext.ApiOutboxMessages.SingleAsync(x => x.Id == messageId);
        persisted.ProcessedAt.Should().NotBeNull();
        persisted.Status.Should().Be(2);
        persisted.Attempts.Should().Be(1);
        persisted.LockId.Should().BeNull();
    }

    [WorkerPostgreSqlIntegrationFact]
    public async Task ExpiredLease_IsRecoveredAfterPreviousWorkerStops()
    {
        await using var container = await StartDatabaseAsync();
        var messageId = await CreateApiOutboxTableAndMessageAsync(
            container.GetConnectionString(),
            attempts: 1,
            lockId: Guid.NewGuid(),
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));
        var publisher = new CountingPublisher();
        await using var context = CreateContext(container.GetConnectionString());

        (await CreateProcessor(context, publisher).ProcessBatchAsync()).Should().Be(1);

        publisher.Count.Should().Be(1);
        await using var assertionContext = CreateContext(container.GetConnectionString());
        var persisted = await assertionContext.ApiOutboxMessages.SingleAsync(x => x.Id == messageId);
        persisted.ProcessedAt.Should().NotBeNull();
        persisted.Attempts.Should().Be(2);
        persisted.LockedUntil.Should().BeNull();
    }

    private static async Task<PostgreSqlContainer> StartDatabaseAsync()
    {
        var container = new PostgreSqlBuilder("postgres:18.6-alpine3.23")
            .WithDatabase("api_outbox_publisher_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        return container;
    }

    private static WorkerDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseNpgsql(connectionString)
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
                BaseRetryDelaySeconds = 1
            }),
            NullLogger<ApiOutboxPublisher>.Instance);

    private static async Task<Guid> CreateApiOutboxTableAndMessageAsync(
        string connectionString,
        int attempts = 0,
        Guid? lockId = null,
        DateTimeOffset? lockedUntil = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand("""
            CREATE TABLE public."OutboxMessages" (
                "Id" uuid PRIMARY KEY,
                "Type" integer NOT NULL,
                "Payload" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "Status" integer NOT NULL,
                "ProcessedAt" timestamp with time zone NULL,
                "Attempts" integer NOT NULL,
                "NextAttemptAt" timestamp with time zone NOT NULL,
                "LockId" uuid NULL,
                "LockedUntil" timestamp with time zone NULL,
                "LastError" character varying(2000) NULL
                ,"CorrelationId" character varying(128) NOT NULL
            );
            """, connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var insert = new NpgsqlCommand("""
            INSERT INTO public."OutboxMessages"
                ("Id", "Type", "Payload", "CreatedAt", "Status", "Attempts",
                 "NextAttemptAt", "LockId", "LockedUntil", "CorrelationId")
            VALUES
                (@id, 1, '{}', @createdAt, 1, @attempts, @nextAttemptAt, @lockId, @lockedUntil,
                 'integration-test');
            """, connection);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("createdAt", now.AddMinutes(-5));
        insert.Parameters.AddWithValue("attempts", attempts);
        insert.Parameters.AddWithValue("nextAttemptAt", now.AddMinutes(-5));
        insert.Parameters.AddWithValue("lockId", (object?)lockId ?? DBNull.Value);
        insert.Parameters.AddWithValue("lockedUntil", (object?)lockedUntil ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync();
        return id;
    }

    private sealed class CountingPublisher : IWorkerIntegrationEventPublisher
    {
        private int _count;
        public int Count => _count;

        public async Task PublishAsync(
            WorkerIntegrationEvent integrationEvent,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            await Task.Delay(100, cancellationToken);
        }
    }
}
