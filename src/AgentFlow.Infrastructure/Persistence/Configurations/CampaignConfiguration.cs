using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.HasKey(c => c.Id);
        b.HasIndex(c => new { c.TenantId, c.IsActive });
        b.Property(c => c.Name).HasMaxLength(300).IsRequired();
        b.Property(c => c.Trigger).HasConversion<string>().HasMaxLength(50);
        b.Property(c => c.Channel).HasConversion<string>().HasMaxLength(50);
        b.Property(c => c.SourceFileName).HasMaxLength(500);
        b.Property(c => c.SourceFilePath).HasMaxLength(1000);
        b.Property(c => c.CreatedByUserId).HasMaxLength(100).IsRequired();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(c => c.LaunchedByUserId).HasMaxLength(100);
        b.HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(c => c.AgentDefinition).WithMany().HasForeignKey(c => c.AgentDefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Contacts).WithOne(cc => cc.Campaign).HasForeignKey(cc => cc.CampaignId);
    }
}
