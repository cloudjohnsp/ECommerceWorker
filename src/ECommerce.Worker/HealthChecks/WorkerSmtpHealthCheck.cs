using System.Net.Sockets;
using ECommerce.Worker.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.HealthChecks;

public sealed class WorkerSmtpHealthCheck(IOptions<WorkerEmailOptions> options) : IHealthCheck
{
    private readonly WorkerEmailOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, cancellationToken);
            return HealthCheckResult.Healthy("SMTP is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("SMTP health check failed.", exception);
        }
    }
}
