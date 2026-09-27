using Microsoft.EntityFrameworkCore;

namespace ECommerce.Worker.Persistence;

public static class WorkerDbContextInboxExtensions
{
    public static async Task AcquireInboxMessageLockAsync(
        this WorkerDbContext dbContext,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsNpgsql())
            return;

        var lockKey = $"worker-inbox:{messageId:N}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
