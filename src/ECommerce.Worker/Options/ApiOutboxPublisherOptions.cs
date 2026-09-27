namespace ECommerce.Worker.Options;

public sealed class ApiOutboxPublisherOptions
{
    public const string SectionName = "ApiOutboxPublisher";

    public bool Enabled { get; init; }
    public int BatchSize { get; init; } = 20;
    public int PollIntervalSeconds { get; init; } = 5;
    public int LockSeconds { get; init; } = 60;
    public int MaximumAttempts { get; init; } = 8;
    public int BaseRetryDelaySeconds { get; init; } = 5;
}
