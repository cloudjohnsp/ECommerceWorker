using ECommerce.Worker.Options;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Messaging;

public sealed class ApiOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<ApiOutboxPublisherOptions> options,
    ILogger<ApiOutboxDispatcher> logger) : BackgroundService
{
    private readonly ApiOutboxPublisherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ApiOutboxPublisher>();
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
