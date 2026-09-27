using ECommerce.Worker.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerce.Worker.HealthChecks;

public sealed class WorkerPostgresHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Worker PostgreSQL schema is reachable.")
                : HealthCheckResult.Unhealthy("Worker PostgreSQL schema is unreachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Worker PostgreSQL health check failed.",
                exception);
        }
    }
}
