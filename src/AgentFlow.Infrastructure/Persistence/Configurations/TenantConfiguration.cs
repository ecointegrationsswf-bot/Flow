using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.HasKey(t => t.Id);
        b.HasIndex(t => t.Slug).IsUnique();
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.Slug).HasMaxLength(100).IsRequired();
        b.Property(t => t.WhatsAppPhoneNumber).HasMaxLength(20);
        b.Property(t => t.WhatsAppApiToken).HasMaxLength(500);
        b.Property(t => t.TimeZone).HasMaxLength(50);
        b.Property(t => t.WhatsAppProvider).HasConversion<string>();
        b.HasMany(t => t.Agents).WithOne(a => a.Tenant).HasForeignKey(a => a.TenantId);
        b.HasMany(t => t.Campaigns).WithOne(c => c.Tenant).HasForeignKey(c => c.TenantId);
    }
}
