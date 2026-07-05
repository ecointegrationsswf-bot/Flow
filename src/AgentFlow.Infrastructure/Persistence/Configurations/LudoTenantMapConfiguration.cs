using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Integración Ludo CRM — Fase 1. Mapeo idempotente ludoTenantId ↔ tenantId.
/// La tabla real se crea por guard idempotente en Program.cs; esta config solo alinea
/// la convención EF (maxlength del campo indexado) para que el DbSet mapee sin migración.
/// </summary>
public class LudoTenantMapConfiguration : IEntityTypeConfiguration<LudoTenantMap>
{
    public void Configure(EntityTypeBuilder<LudoTenantMap> b)
    {
        b.HasKey(m => m.Id);
        b.Property(m => m.LudoTenantId).HasMaxLength(64).IsRequired();
        b.Property(m => m.TipoNegocio).HasMaxLength(50);
        b.HasIndex(m => m.LudoTenantId).IsUnique();
    }
}
