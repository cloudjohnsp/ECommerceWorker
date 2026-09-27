using ECommerce.Worker.Options;
using ECommerce.Worker.Messaging;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Notifications;

public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationProcessorOptions> options,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private readonly NotificationProcessorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<NotificationOutboxProcessor>();
                await processor.ProcessBatchAsync(stoppingToken);
                var eventProcessor = scope.ServiceProvider
                    .GetRequiredService<WorkerIntegrationEventOutboxProcessor>();
                await eventProcessor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
