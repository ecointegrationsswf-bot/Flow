using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class InboundMessageQueueItemConfiguration : IEntityTypeConfiguration<InboundMessageQueueItem>
{
    public void Configure(EntityTypeBuilder<InboundMessageQueueItem> b)
    {
        b.ToTable("InboundMessageQueueItems");
        b.HasKey(x => x.Id);

        b.Property(x => x.FromPhone).HasMaxLength(30).IsRequired();
        b.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        b.Property(x => x.ClientName).HasMaxLength(200);
        b.Property(x => x.ExternalMessageId).HasMaxLength(100);
        b.Property(x => x.MediaType).HasMaxLength(30);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ClaimedBy).HasMaxLength(80);
        b.Property(x => x.LastErrorStep).HasMaxLength(50);

        b.HasIndex(x => new { x.Status, x.LastReceivedAt })
            .HasDatabaseName("IX_IMQ_Status_LastAt");
        b.HasIndex(x => new { x.TenantId, x.FromPhone, x.Status })
            .HasDatabaseName("IX_IMQ_Tenant_Phone_Status");
    }
}
