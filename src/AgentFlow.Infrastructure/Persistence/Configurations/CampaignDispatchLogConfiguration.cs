using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class CampaignDispatchLogConfiguration : IEntityTypeConfiguration<CampaignDispatchLog>
{
    public void Configure(EntityTypeBuilder<CampaignDispatchLog> b)
    {
        b.HasKey(l => l.Id);
        b.HasIndex(l => new { l.CampaignId, l.OccurredAt });
        b.HasIndex(l => l.CampaignContactId);
        b.Property(l => l.PhoneNumber).HasMaxLength(20).IsRequired();
        b.Property(l => l.Status).HasMaxLength(20).IsRequired();
        b.Property(l => l.ExternalMessageId).HasMaxLength(200);
        b.Property(l => l.ErrorDetail).HasMaxLength(2000);
        b.Property(l => l.PromptSnapshot).HasColumnType("nvarchar(max)");
        b.Property(l => l.ContactDataSnapshot).HasColumnType("nvarchar(max)");
        b.Property(l => l.GeneratedMessage).HasColumnType("nvarchar(max)");
        b.Property(l => l.UltraMsgResponse).HasColumnType("nvarchar(max)");
    }
}
