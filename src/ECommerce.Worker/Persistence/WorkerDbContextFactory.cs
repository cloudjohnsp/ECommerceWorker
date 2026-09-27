using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ECommerce.Worker.Persistence;

public sealed class WorkerDbContextFactory : IDesignTimeDbContextFactory<WorkerDbContext>
{
    public const string ConnectionStringEnvironmentVariable =
        "ECOMMERCE_WORKER_DESIGN_TIME_CONNECTION_STRING";

    public WorkerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Host=localhost;Port=5432;Database=ecommerce;Username=design-time";
        }

        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WorkerDbContext.SchemaName))
            .Options;

        return new WorkerDbContext(options);
    }
}
