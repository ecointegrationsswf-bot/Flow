using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class SystemAuditLogConfiguration : IEntityTypeConfiguration<SystemAuditLog>
{
    public void Configure(EntityTypeBuilder<SystemAuditLog> b)
    {
        // Tabla en singular (no SystemAuditLogs) — alineado con cómo la crea el
        // self-healing schema. EF Core por default pluraliza el nombre del DbSet
        // si no se especifica.
        b.ToTable("SystemAuditLog");
        b.HasKey(x => x.Id);
        b.Property(x => x.Category).HasMaxLength(50).IsRequired();
        b.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.RelatedEntityType).HasMaxLength(50);
        // StackTrace y Context van como NVARCHAR(MAX) — no se acota tipo, solo el
        // truncado en código (4000 chars por seguridad).

        // Índices: las consultas típicas filtran por fecha + tenant + categoría.
        b.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("IX_SystemAudit_Time");
        b.HasIndex(x => new { x.TenantId, x.OccurredAtUtc }).HasDatabaseName("IX_SystemAudit_TenantTime");
        b.HasIndex(x => new { x.Category, x.OccurredAtUtc }).HasDatabaseName("IX_SystemAudit_CategoryTime");
    }
}
