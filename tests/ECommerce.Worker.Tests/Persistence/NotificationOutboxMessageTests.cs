using ECommerce.Worker.Persistence.Entities;
using FluentAssertions;

namespace ECommerce.Worker.Tests.Persistence;

public sealed class NotificationOutboxMessageTests
{
    [Fact]
    public void MarkFailed_ReleasesLeaseAndSchedulesRetry()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new NotificationOutboxMessage(
            Guid.NewGuid(),
            "customer@example.com",
            "Subject",
            "Body",
            now);
        message.Lock(now.AddMinutes(1));

        message.MarkFailed("SMTP unavailable", now.AddMinutes(2));

        message.Attempts.Should().Be(1);
        message.LockedUntil.Should().BeNull();
        message.NextAttemptAt.Should().Be(now.AddMinutes(2));
        message.LastError.Should().Be("SMTP unavailable");
    }

    [Fact]
    public void MarkSent_CompletesMessageAndReleasesLease()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new NotificationOutboxMessage(
            Guid.NewGuid(),
            "customer@example.com",
            "Subject",
            "Body",
            now);
        message.Lock(now.AddMinutes(1));

        message.MarkSent(now);

        message.Attempts.Should().Be(1);
        message.SentAt.Should().Be(now);
        message.LockedUntil.Should().BeNull();
        message.LastError.Should().BeNull();
    }
}
