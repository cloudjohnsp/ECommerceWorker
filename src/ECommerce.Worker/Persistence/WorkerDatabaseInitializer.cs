using Microsoft.EntityFrameworkCore;

namespace ECommerce.Worker.Persistence;

public static class WorkerDatabaseInitializer
{
    public static async Task InitializeWorkerDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        const int maximumAttempts = 10;
        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("WorkerDatabaseInitializer");
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Worker database migrations are up to date.");
                return;
            }
            catch (Exception exception) when (attempt < maximumAttempts)
            {
                logger.LogWarning(
                    exception,
                    "Worker database initialization attempt {Attempt}/{MaximumAttempts} failed.",
                    attempt,
                    maximumAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }
}
