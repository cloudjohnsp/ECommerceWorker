using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Worker.Persistence.Configurations;

public sealed class WorkerIntegrationOutboxMessageConfiguration
    : IEntityTypeConfiguration<WorkerIntegrationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<WorkerIntegrationOutboxMessage> builder)
    {
        builder.ToTable("integration_outbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.CreatedAt).IsRequired();
        builder.Property(message => message.NextAttemptAt).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2000);
        builder.HasIndex(message => new
        {
            message.ProcessedAt,
            message.NextAttemptAt,
            message.LockedUntil
        });
    }
}
