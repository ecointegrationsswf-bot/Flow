using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Integración Ludo CRM — Fase 1 (consumida en Fase 4). Cola de reintento para llamadas
/// de salida a Ludo. Índice por (Status, NextAttemptAt) para que el drenador del outbox
/// barra eficientemente los Pending vencidos. Tabla creada por guard idempotente en Program.cs.
/// </summary>
public class LudoOutboxItemConfiguration : IEntityTypeConfiguration<LudoOutboxItem>
{
    public void Configure(EntityTypeBuilder<LudoOutboxItem> b)
    {
        b.HasKey(o => o.Id);
        b.Property(o => o.PhoneE164).HasMaxLength(32).IsRequired();
        b.Property(o => o.ActionSlug).HasMaxLength(64).IsRequired();
        b.Property(o => o.Status).HasMaxLength(20).IsRequired();
        b.HasIndex(o => new { o.Status, o.NextAttemptAt });
    }
}
