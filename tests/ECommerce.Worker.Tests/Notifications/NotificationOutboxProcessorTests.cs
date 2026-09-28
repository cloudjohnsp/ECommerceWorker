using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Notifications;
using ECommerce.Worker.Options;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Tests.Notifications;

public sealed class NotificationOutboxProcessorTests
{
    [Fact]
    public async Task ProcessBatch_WhenDeliverySucceeds_MarksNotificationAsSent()
    {
        await using var context = CreateContext();
        var message = CreateMessage();
        context.NotificationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var sender = new RecordingEmailSender();
        var processor = CreateProcessor(context, sender);

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        message.SentAt.Should().NotBeNull();
        message.Attempts.Should().Be(1);
        sender.Deliveries.Should().ContainSingle();
        var sentEvent = await context.IntegrationOutboxMessages.SingleAsync();
        sentEvent.Id.Should().Be(message.Id);
        sentEvent.Type.Should().Be("email.sent");
        sentEvent.CorrelationId.Should().Be("checkout-123");
        var payload = JsonSerializer.Deserialize<EmailSentIntegrationEventPayload>(sentEvent.Payload);
        payload!.DeliveryId.Should().Be(message.Id);
        payload.Category.Should().Be(EmailDeliveryCategories.OrderNotification);
        sentEvent.Payload.Should().NotContain(message.Recipient);
        sentEvent.Payload.Should().NotContain(message.Subject);
        sentEvent.Payload.Should().NotContain(message.Body);
    }

    [Fact]
    public async Task ProcessBatch_WhenDeliveryFails_SchedulesExponentialRetry()
    {
        await using var context = CreateContext();
        var message = CreateMessage();
        context.NotificationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var processor = CreateProcessor(context, new FailingEmailSender());
        var before = DateTimeOffset.UtcNow;

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(1);
        message.SentAt.Should().BeNull();
        message.Attempts.Should().Be(1);
        message.LastError.Should().Be("SMTP unavailable");
        message.NextAttemptAt.Should().BeAfter(before.AddSeconds(9));
        message.LockedUntil.Should().BeNull();
        (await context.IntegrationOutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatch_DoesNotClaimNotificationAtMaximumAttempts()
    {
        await using var context = CreateContext();
        var message = CreateMessage();
        message.MarkFailed("first", DateTimeOffset.UtcNow.AddMinutes(-1));
        message.MarkFailed("second", DateTimeOffset.UtcNow.AddMinutes(-1));
        context.NotificationOutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var sender = new RecordingEmailSender();
        var processor = CreateProcessor(context, sender, maximumAttempts: 2);

        var processed = await processor.ProcessBatchAsync();

        processed.Should().Be(0);
        sender.Deliveries.Should().BeEmpty();
    }

    private static NotificationOutboxProcessor CreateProcessor(
        WorkerDbContext context,
        IOrderEmailSender sender,
        int maximumAttempts = 8) => new(
        context,
        sender,
        Microsoft.Extensions.Options.Options.Create(new NotificationProcessorOptions
        {
            BatchSize = 10,
            LockSeconds = 60,
            MaximumAttempts = maximumAttempts
        }),
        NullLogger<NotificationOutboxProcessor>.Instance);

    private static WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkerDbContext(options);
    }

    private static NotificationOutboxMessage CreateMessage() => new(
        Guid.NewGuid(),
        "customer@example.com",
        "Subject",
        "Body",
        DateTimeOffset.UtcNow.AddSeconds(-1),
        "checkout-123");

    private sealed class RecordingEmailSender : IOrderEmailSender
    {
        public List<string> Deliveries { get; } = [];

        public Task SendAsync(
            string recipient,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            Deliveries.Add(recipient);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEmailSender : IOrderEmailSender
    {
        public Task SendAsync(
            string recipient,
            string subject,
            string body,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SMTP unavailable");
    }
}
