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

        // Campos del indexado RAG (Día 1 del rollout). Truncamos la columna
        // de error a 500 chars en BD para no explotar si el stack es enorme — el
        // indexer también la trunca antes de persistir.
        b.Property(d => d.IndexingError).HasMaxLength(500);

        b.HasOne(d => d.CampaignTemplate)
            .WithMany(t => t.Documents)
            .HasForeignKey(d => d.CampaignTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Chunks: cascade delete. Si se borra el PDF, se borran sus embeddings.
        b.HasMany(d => d.Chunks)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(d => d.CampaignTemplateId);
        b.HasIndex(d => d.TenantId);
    }
}
