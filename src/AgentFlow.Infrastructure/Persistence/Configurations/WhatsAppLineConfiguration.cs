using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class WhatsAppLineConfiguration : IEntityTypeConfiguration<WhatsAppLine>
{
    public void Configure(EntityTypeBuilder<WhatsAppLine> b)
    {
        b.HasKey(l => l.Id);
        b.Property(l => l.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(l => l.PhoneNumber).HasMaxLength(20);
        b.Property(l => l.InstanceId).HasMaxLength(200).IsRequired();
        b.Property(l => l.ApiToken).HasMaxLength(500).IsRequired();
        b.Property(l => l.Provider).HasConversion<string>().HasMaxLength(50);
        b.Property(l => l.LastStatus).HasMaxLength(40);

        // Credenciales Meta (nullable — solo líneas MetaCloudApi).
        b.Property(l => l.MetaWabaId).HasMaxLength(100);
        b.Property(l => l.MetaAccessToken); // nvarchar(max) — el token de Graph API es largo
        b.Property(l => l.MetaAppSecret).HasMaxLength(200);
        b.Property(l => l.MetaBusinessId).HasMaxLength(100);

        b.HasOne(l => l.Tenant)
            .WithMany(t => t.WhatsAppLines)
            .HasForeignKey(l => l.TenantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(l => l.TenantId);
        b.HasIndex(l => new { l.TenantId, l.InstanceId }).IsUnique();
    }
}
