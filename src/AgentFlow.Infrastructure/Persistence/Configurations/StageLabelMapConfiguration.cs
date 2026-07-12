using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Integración Ludo CRM — Fase 1. Homologación etapa Ludo ↔ etiqueta TalkIA, por tenant.
/// La clave de homologación es LudoStageId (estable). Índice único por (TenantId, LudoStageId)
/// para que la reconciliación (Dirección C) haga upsert determinista.
/// Tabla creada por guard idempotente en Program.cs.
/// </summary>
public class StageLabelMapConfiguration : IEntityTypeConfiguration<StageLabelMap>
{
    public void Configure(EntityTypeBuilder<StageLabelMap> b)
    {
        b.HasKey(s => s.Id);
        b.Property(s => s.LudoStageId).HasMaxLength(64).IsRequired();
        b.Property(s => s.Nombre).HasMaxLength(100).IsRequired();
        b.HasIndex(s => new { s.TenantId, s.LudoStageId }).IsUnique();
    }
}
