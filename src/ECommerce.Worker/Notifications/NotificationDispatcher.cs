using ECommerce.Worker.Options;
using ECommerce.Worker.Messaging;
using ECommerce.Worker.Resilience;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Notifications;

public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationProcessorOptions> options,
    WorkerOperationDrain drain,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private readonly NotificationProcessorOptions _options = options.Value;

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
                    var processor = scope.ServiceProvider.GetRequiredService<NotificationOutboxProcessor>();
                    await processor.ProcessBatchAsync(lease.CancellationToken);
                    var eventProcessor = scope.ServiceProvider
                        .GetRequiredService<WorkerIntegrationEventOutboxProcessor>();
                    await eventProcessor.ProcessBatchAsync(lease.CancellationToken);
                }
                catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Notification outbox processing failed.");
                }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
