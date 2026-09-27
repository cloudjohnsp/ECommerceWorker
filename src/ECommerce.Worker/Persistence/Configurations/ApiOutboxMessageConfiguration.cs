using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Worker.Persistence.Configurations;

public sealed class ApiOutboxMessageConfiguration : IEntityTypeConfiguration<ApiOutboxMessage>
{
    public void Configure(EntityTypeBuilder<ApiOutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", "public", table => table.ExcludeFromMigrations());
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.CreatedAt).IsRequired();
        builder.Property(message => message.NextAttemptAt).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(2000);
    }
}
