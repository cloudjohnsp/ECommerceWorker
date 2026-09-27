namespace ECommerce.Worker.Persistence.Entities;

public sealed class WorkerIntegrationOutboxMessage
{
    private WorkerIntegrationOutboxMessage()
    {
    }

    public WorkerIntegrationOutboxMessage(
        Guid id,
        string type,
        string payload,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Message ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Event type is required.", nameof(type));
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Event payload is required.", nameof(payload));

        Id = id;
        Type = type.Trim();
        Payload = payload;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public void Lock(DateTimeOffset lockedUntil) => LockedUntil = lockedUntil;

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        Attempts++;
        ProcessedAt = processedAt;
        LockedUntil = null;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset nextAttemptAt)
    {
        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? "Unknown integration event publication error."
            : error.Trim();

        Attempts++;
        LastError = normalizedError[..Math.Min(normalizedError.Length, 2000)];
        NextAttemptAt = nextAttemptAt;
        LockedUntil = null;
    }
}
