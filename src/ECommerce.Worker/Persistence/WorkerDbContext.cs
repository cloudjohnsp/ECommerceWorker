using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Worker.Persistence;

public sealed class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public const string SchemaName = "worker";

    public DbSet<ConsumedIntegrationEvent> ConsumedIntegrationEvents => Set<ConsumedIntegrationEvent>();
    public DbSet<OrderProjection> OrderProjections => Set<OrderProjection>();
    public DbSet<InventoryProjection> InventoryProjections => Set<InventoryProjection>();
    public DbSet<EmailDeliveryProjection> EmailDeliveryProjections => Set<EmailDeliveryProjection>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<NotificationOutboxMessage> NotificationOutboxMessages => Set<NotificationOutboxMessage>();
    public DbSet<WorkerIntegrationOutboxMessage> IntegrationOutboxMessages =>
        Set<WorkerIntegrationOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
