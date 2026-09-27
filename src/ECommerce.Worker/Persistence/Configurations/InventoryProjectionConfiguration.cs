using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Worker.Persistence.Configurations;

public sealed class InventoryProjectionConfiguration
    : IEntityTypeConfiguration<InventoryProjection>
{
    public void Configure(EntityTypeBuilder<InventoryProjection> builder)
    {
        builder.ToTable("inventory_projections", table =>
            table.HasCheckConstraint(
                "CK_inventory_projections_AvailableStock",
                "\"AvailableStock\" >= 0"));
        builder.HasKey(projection => projection.ProductId);
        builder.Property(projection => projection.ProductId).ValueGeneratedNever();
        builder.Property(projection => projection.LastReason).HasMaxLength(50).IsRequired();
        builder.Property(projection => projection.UpdatedAt).IsRequired();
        builder.HasIndex(projection => projection.UpdatedAt);
    }
}
