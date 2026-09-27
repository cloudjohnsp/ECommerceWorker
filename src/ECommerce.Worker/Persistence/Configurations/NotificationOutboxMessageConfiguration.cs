using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Worker.Persistence.Configurations;

public sealed class NotificationOutboxMessageConfiguration
    : IEntityTypeConfiguration<NotificationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxMessage> builder)
    {
        builder.ToTable("notification_outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.HasIndex(x => x.SourceMessageId).IsUnique();
        builder.HasIndex(x => new { x.SentAt, x.NextAttemptAt, x.LockedUntil });
    }
}
