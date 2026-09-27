namespace ECommerce.Worker.Options;

public sealed class WorkerObservabilityOptions
{
    public const string SectionName = "Observability";

    public string ServiceName { get; init; } = "ECommerce.Worker";
    public bool EnablePrometheus { get; init; } = true;
    public string? OtlpEndpoint { get; init; }
}
