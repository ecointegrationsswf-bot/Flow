using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class CampaignTemplateDocumentConfiguration : IEntityTypeConfiguration<CampaignTemplateDocument>
{
    public void Configure(EntityTypeBuilder<CampaignTemplateDocument> b)
    {
        b.HasKey(d => d.Id);
        b.Property(d => d.FileName).HasMaxLength(500).IsRequired();
        b.Property(d => d.BlobUrl).HasMaxLength(2000).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(100);
        b.Property(d => d.Description).HasMaxLength(500);

        b.HasOne(d => d.CampaignTemplate)
            .WithMany(t => t.Documents)
            .HasForeignKey(d => d.CampaignTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(d => d.CampaignTemplateId);
        b.HasIndex(d => d.TenantId);
    }
}
