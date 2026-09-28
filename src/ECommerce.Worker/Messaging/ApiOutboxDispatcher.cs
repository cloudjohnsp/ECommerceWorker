using ECommerce.Worker.Options;
using ECommerce.Worker.Resilience;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Messaging;

public sealed class ApiOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<ApiOutboxPublisherOptions> options,
    WorkerOperationDrain drain,
    ILogger<ApiOutboxDispatcher> logger) : BackgroundService
{
    private readonly ApiOutboxPublisherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var registration = stoppingToken.Register(drain.StopAccepting);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        do
        {
            if (!drain.TryBegin(out var lease))
                break;
            using (lease)
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ApiOutboxPublisher>();
                    await processor.ProcessBatchAsync(lease.CancellationToken);
                }
                catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "API outbox publishing failed.");
                }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
