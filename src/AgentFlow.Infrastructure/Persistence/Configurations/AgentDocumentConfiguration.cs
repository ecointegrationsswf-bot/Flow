using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class AgentDocumentConfiguration : IEntityTypeConfiguration<AgentDocument>
{
    public void Configure(EntityTypeBuilder<AgentDocument> b)
    {
        b.HasKey(d => d.Id);
        b.Property(d => d.FileName).HasMaxLength(500).IsRequired();
        b.Property(d => d.BlobUrl).HasMaxLength(2000).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(100);

        b.HasOne(d => d.AgentDefinition)
            .WithMany(a => a.Documents)
            .HasForeignKey(d => d.AgentDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(d => d.AgentDefinitionId);
        b.HasIndex(d => d.TenantId);
    }
}
