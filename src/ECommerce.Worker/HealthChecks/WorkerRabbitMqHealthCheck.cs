using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace ECommerce.Worker.HealthChecks;

public sealed class WorkerRabbitMqHealthCheck(ConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ is reachable.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ health check failed.", exception);
        }
    }
}
