using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF para <see cref="CampaignTemplateDocumentChunk"/> — la unidad
/// básica del índice RAG (chunk de texto + embedding).
///
/// Notas de diseño:
/// - El embedding se persiste como VARBINARY(MAX). Para text-embedding-3-small son
///   1536 floats × 4 bytes = 6144 bytes; un poco menos que MAX pero MAX evita tener
///   que cambiar el tipo si se prueba un modelo de embedding más grande (large = 3072).
/// - Índices: por TenantId+CampaignTemplateId para filtros rápidos al retrieval, y
///   por DocumentId para cascade delete al borrar un PDF.
/// - El cosine similarity se hace en C# memoria — no hay GIST/IVFFLAT como en
///   pgvector. SQL Server (versión hosting site4now) no tiene VECTOR todavía.
/// </summary>
public class CampaignTemplateDocumentChunkConfiguration
    : IEntityTypeConfiguration<CampaignTemplateDocumentChunk>
{
    public void Configure(EntityTypeBuilder<CampaignTemplateDocumentChunk> b)
    {
        b.HasKey(c => c.Id);

        b.Property(c => c.Text).IsRequired();                   // NVARCHAR(MAX) por default
        b.Property(c => c.TextHash).HasMaxLength(64).IsRequired();
        b.Property(c => c.Embedding).IsRequired();              // VARBINARY(MAX)

        // Filtro principal del retrieval: top-K por (tenant, template) → consulta común.
        b.HasIndex(c => new { c.TenantId, c.CampaignTemplateId })
            .HasDatabaseName("IX_DocChunks_TenantTemplate");

        // Cascade delete por documento ya viene del lado Document; mantenemos índice
        // simple para acelerar el DELETE bulk al reindexar.
        b.HasIndex(c => c.DocumentId)
            .HasDatabaseName("IX_DocChunks_Document");

        // Permite dedup al re-indexar (mismo texto → mismo hash → reusamos embedding).
        b.HasIndex(c => new { c.DocumentId, c.TextHash })
            .HasDatabaseName("IX_DocChunks_DocumentHash");
    }
}
