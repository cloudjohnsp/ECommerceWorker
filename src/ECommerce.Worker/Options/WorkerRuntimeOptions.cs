namespace ECommerce.Worker.Options;

public sealed class WorkerRuntimeOptions
{
    public const string SectionName = "WorkerRuntime";

    public int ShutdownDrainSeconds { get; init; } = 30;
    public int ConsumerConcurrency { get; init; } = 1;
    public int DatabasePoolSize { get; init; } = 32;
    public int DatabaseCommandTimeoutSeconds { get; init; } = 30;
    public int RabbitMqConnectionTimeoutSeconds { get; init; } = 10;
}
