using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Worker.Persistence.Configurations;

public sealed class OrderProjectionConfiguration : IEntityTypeConfiguration<OrderProjection>
{
    public void Configure(EntityTypeBuilder<OrderProjection> builder)
    {
        builder.ToTable("order_projections");
        builder.HasKey(x => x.OrderId);
        builder.Property(x => x.OrderId).ValueGeneratedNever();
        builder.Property(x => x.CustomerEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Total).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
