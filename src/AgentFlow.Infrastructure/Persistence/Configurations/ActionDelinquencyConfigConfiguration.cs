using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ActionDelinquencyConfigConfiguration : IEntityTypeConfiguration<ActionDelinquencyConfig>
{
    public void Configure(EntityTypeBuilder<ActionDelinquencyConfig> b)
    {
        b.ToTable("ActionDelinquencyConfigs");
        b.HasKey(x => x.Id);

        // Un config por (TenantId, ActionDefinitionId)
        b.HasIndex(x => new { x.TenantId, x.ActionDefinitionId }).IsUnique();

        b.Property(x => x.CodigoPais).HasMaxLength(10).HasDefaultValue("507");
        b.Property(x => x.ItemsJsonPath).HasMaxLength(500);
        b.Property(x => x.CampaignNamePattern).HasMaxLength(300);
        b.Property(x => x.NotificationEmail).HasMaxLength(200);
        b.Property(x => x.DownloadWebhookUrl).HasMaxLength(2000);
        b.Property(x => x.DownloadWebhookMethod).HasMaxLength(10).HasDefaultValue("GET");
        b.Property(x => x.DownloadWebhookHeaders).HasMaxLength(2000);

        b.HasOne(x => x.ActionDefinition)
            .WithMany()
            .HasForeignKey(x => x.ActionDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CampaignTemplate)
            .WithMany()
            .HasForeignKey(x => x.CampaignTemplateId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.AgentDefinition)
            .WithMany()
            .HasForeignKey(x => x.AgentDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
