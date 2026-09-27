using ECommerce.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Worker.Persistence.Configurations;

public sealed class ConsumedIntegrationEventConfiguration
    : IEntityTypeConfiguration<ConsumedIntegrationEvent>
{
    public void Configure(EntityTypeBuilder<ConsumedIntegrationEvent> builder)
    {
        builder.ToTable("consumed_integration_events");
        builder.HasKey(x => x.MessageId);
        builder.Property(x => x.MessageId).ValueGeneratedNever();
        builder.Property(x => x.EventType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ConsumedAt).IsRequired();
    }
}
