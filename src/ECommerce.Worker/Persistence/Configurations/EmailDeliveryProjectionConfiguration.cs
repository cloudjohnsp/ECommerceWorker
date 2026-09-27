using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Worker.Persistence.Configurations;

public sealed class EmailDeliveryProjectionConfiguration
    : IEntityTypeConfiguration<EmailDeliveryProjection>
{
    public void Configure(EntityTypeBuilder<EmailDeliveryProjection> builder)
    {
        builder.ToTable("email_delivery_projections");
        builder.HasKey(delivery => delivery.DeliveryId);
        builder.Property(delivery => delivery.DeliveryId).ValueGeneratedNever();
        builder.Property(delivery => delivery.Category).HasMaxLength(50).IsRequired();
        builder.Property(delivery => delivery.SentAt).IsRequired();
        builder.HasIndex(delivery => delivery.SentAt);
    }
}
