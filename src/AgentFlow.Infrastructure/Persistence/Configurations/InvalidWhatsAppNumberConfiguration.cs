using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class InvalidWhatsAppNumberConfiguration : IEntityTypeConfiguration<InvalidWhatsAppNumber>
{
    public void Configure(EntityTypeBuilder<InvalidWhatsAppNumber> b)
    {
        b.ToTable("InvalidWhatsAppNumbers");
        b.HasKey(x => x.Id);

        b.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);

        // Unicidad relativa: el mismo número puede existir como entrada cross-tenant
        // (TenantId=null) Y como entrada tenant-specific (TenantId=X). El validator
        // consulta primero la entrada del tenant; si no hay, busca la global.
        // Por eso el índice único es compuesto.
        b.HasIndex(x => new { x.PhoneNumber, x.TenantId })
            .HasDatabaseName("UX_InvalidWANumber_Phone_Tenant")
            .IsUnique();

        // Índice secundario para la consulta del listado por activos + creación reciente.
        b.HasIndex(x => new { x.IsActive, x.LastCheckedAt })
            .HasDatabaseName("IX_InvalidWANumber_Active_LastCheck");
    }
}
