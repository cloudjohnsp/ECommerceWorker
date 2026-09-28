namespace ECommerce.Worker.Persistence.Entities;

public sealed class ApiOutboxMessage
{
    public const int PendingStatus = 1;
    public const int ProcessedStatus = 2;

    private ApiOutboxMessage()
    {
    }

    public ApiOutboxMessage(Guid id, int type, string payload, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Message ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload is required.", nameof(payload));

        Id = id;
        Type = type;
        Payload = payload;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
        Status = PendingStatus;
        CorrelationId = id.ToString("N");
    }

    public Guid Id { get; private set; }
    public int Type { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public int Status { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public Guid? LockId { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? LastError { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;

    public string? GetEventType() => Type switch
    {
        1 => "order.created",
        2 => "order.updated",
        3 => "order.deleted",
        7 => "order.paid",
        8 => "payment.failed",
        9 => "order.refunded",
        10 => "order.cancelled",
        11 => "stock.updated",
        12 => "email.sent",
        _ => null
    };

    public void Claim(Guid lockId, DateTimeOffset lockedUntil)
    {
        LockId = lockId;
        LockedUntil = lockedUntil;
        Attempts++;
    }

    public void MarkProcessed(DateTimeOffset processedAt)
    {
        Status = ProcessedStatus;
        ProcessedAt = processedAt;
        UpdatedAt = processedAt;
        LockId = null;
        LockedUntil = null;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset nextAttemptAt, DateTimeOffset failedAt)
    {
        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? "Unknown API outbox publication error."
            : error.Trim();

        LastError = normalizedError[..Math.Min(normalizedError.Length, 2000)];
        NextAttemptAt = nextAttemptAt;
        UpdatedAt = failedAt;
        LockId = null;
        LockedUntil = null;
    }
}
