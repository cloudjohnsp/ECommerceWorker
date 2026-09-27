namespace ECommerce.Worker.Options;

public sealed class NotificationProcessorOptions
{
    public const string SectionName = "NotificationProcessor";

    public int BatchSize { get; init; } = 20;
    public int PollIntervalSeconds { get; init; } = 5;
    public int LockSeconds { get; init; } = 60;
    public int MaximumAttempts { get; init; } = 8;
}
