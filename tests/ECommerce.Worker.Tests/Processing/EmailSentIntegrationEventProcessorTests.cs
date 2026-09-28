using System.Text.Json;
using ECommerce.Worker.Contracts;
using ECommerce.Worker.Persistence;
using ECommerce.Worker.Processing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Worker.Tests.Processing;

public sealed class EmailSentIntegrationEventProcessorTests
{
    [Fact]
    public async Task EmailSent_PersistsProjectionAndInboxAtomically()
    {
        await using var context = CreateContext();
        var processor = new EmailSentIntegrationEventProcessor(context);
        var messageId = Guid.NewGuid();
        var payload = CreatePayload(EmailDeliveryCategories.OrderNotification);

        var result = await processor.ProcessAsync(
            messageId,
            EmailSentIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(payload));

        result.Should().Be(IntegrationEventProcessingResult.Processed);
        var projection = await context.EmailDeliveryProjections.SingleAsync();
        projection.DeliveryId.Should().Be(payload.DeliveryId);
        projection.Category.Should().Be(payload.Category);
        projection.SentAt.Should().Be(payload.OccurredAt);
        (await context.ConsumedIntegrationEvents.SingleAsync()).MessageId.Should().Be(messageId);
        (await context.AnalyticsEvents.SingleAsync()).EventType
            .Should().Be(IntegrationEventTypes.EmailSent);
    }

    [Fact]
    public async Task ReplayedMessage_IsIgnoredWithoutDuplicatingProjection()
    {
        await using var context = CreateContext();
        var processor = new EmailSentIntegrationEventProcessor(context);
        var messageId = Guid.NewGuid();
        var body = JsonSerializer.SerializeToUtf8Bytes(
            CreatePayload(EmailDeliveryCategories.EmailConfirmation));

        await processor.ProcessAsync(messageId, EmailSentIntegrationEventProcessor.EventType, body);
        var result = await processor.ProcessAsync(
            messageId,
            EmailSentIntegrationEventProcessor.EventType,
            body);

        result.Should().Be(IntegrationEventProcessingResult.AlreadyProcessed);
        (await context.EmailDeliveryProjections.CountAsync()).Should().Be(1);
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(1);
        (await context.AnalyticsEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OlderEvent_IsConsumedWithoutRegressingProjection()
    {
        await using var context = CreateContext();
        var processor = new EmailSentIntegrationEventProcessor(context);
        var current = CreatePayload(EmailDeliveryCategories.PasswordReset);
        await processor.ProcessAsync(
            Guid.NewGuid(),
            EmailSentIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(current));
        var older = current with
        {
            Category = EmailDeliveryCategories.EmailConfirmation,
            OccurredAt = current.OccurredAt.AddSeconds(-1)
        };

        await processor.ProcessAsync(
            Guid.NewGuid(),
            EmailSentIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(older));

        var projection = await context.EmailDeliveryProjections.SingleAsync();
        projection.Category.Should().Be(EmailDeliveryCategories.PasswordReset);
        projection.SentAt.Should().Be(current.OccurredAt);
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task UnsupportedCategory_IsRejectedWithoutPersistingInboxEntry()
    {
        await using var context = CreateContext();
        var processor = new EmailSentIntegrationEventProcessor(context);
        var payload = CreatePayload("unknown");

        var action = () => processor.ProcessAsync(
            Guid.NewGuid(),
            EmailSentIntegrationEventProcessor.EventType,
            JsonSerializer.SerializeToUtf8Bytes(payload));

        await action.Should().ThrowAsync<InvalidIntegrationEventException>();
        (await context.EmailDeliveryProjections.CountAsync()).Should().Be(0);
        (await context.ConsumedIntegrationEvents.CountAsync()).Should().Be(0);
    }

    private static WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WorkerDbContext(options);
    }

    private static EmailSentIntegrationEventPayload CreatePayload(string category) => new(
        Guid.NewGuid(),
        category,
        DateTimeOffset.UtcNow);
}
