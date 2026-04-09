using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class AgentRegistryEntryConfiguration : IEntityTypeConfiguration<AgentRegistryEntry>
{
    public void Configure(EntityTypeBuilder<AgentRegistryEntry> b)
    {
        b.ToTable("AgentRegistryEntries");
        b.HasKey(r => r.Id);

        b.Property(r => r.Slug).HasMaxLength(50).IsRequired();
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.Capabilities).HasMaxLength(500).IsRequired();

        b.HasOne(r => r.Tenant).WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.AgentDefinition).WithMany().HasForeignKey(r => r.AgentDefinitionId).OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(r => new { r.TenantId, r.Slug }).IsUnique();
        b.HasIndex(r => r.TenantId);
    }
}
