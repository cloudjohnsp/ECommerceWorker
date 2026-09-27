namespace ECommerce.Worker.Persistence.Entities;

public sealed class NotificationOutboxMessage
{
    private NotificationOutboxMessage()
    {
    }

    public NotificationOutboxMessage(
        Guid sourceMessageId,
        string recipient,
        string subject,
        string body,
        DateTimeOffset createdAt)
    {
        if (sourceMessageId == Guid.Empty)
            throw new ArgumentException("Source message ID is required.", nameof(sourceMessageId));
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("Recipient is required.", nameof(recipient));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body is required.", nameof(body));

        Id = Guid.NewGuid();
        SourceMessageId = sourceMessageId;
        Recipient = recipient.Trim().ToLowerInvariant();
        Subject = subject.Trim();
        Body = body.Trim();
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid SourceMessageId { get; private set; }
    public string Recipient { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public void Lock(DateTimeOffset lockedUntil) => LockedUntil = lockedUntil;

    public void MarkSent(DateTimeOffset sentAt)
    {
        Attempts++;
        SentAt = sentAt;
        LockedUntil = null;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset nextAttemptAt)
    {
        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? "Unknown notification delivery error."
            : error.Trim();

        Attempts++;
        LastError = normalizedError[..Math.Min(normalizedError.Length, 2000)];
        NextAttemptAt = nextAttemptAt;
        LockedUntil = null;
    }
}
