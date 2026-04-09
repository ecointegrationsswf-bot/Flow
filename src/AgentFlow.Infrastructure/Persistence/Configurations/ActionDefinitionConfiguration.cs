using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ActionDefinitionConfiguration : IEntityTypeConfiguration<ActionDefinition>
{
    public void Configure(EntityTypeBuilder<ActionDefinition> b)
    {
        b.ToTable("ActionDefinitions");
        b.HasKey(a => a.Id);
        b.Property(a => a.TenantId).IsRequired();
        b.HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
        b.Property(a => a.Name).HasMaxLength(100).IsRequired();
        b.Property(a => a.Description).HasMaxLength(500);
        b.Property(a => a.WebhookUrl).HasMaxLength(500);
        b.Property(a => a.WebhookMethod).HasMaxLength(10);
        b.HasIndex(a => new { a.TenantId, a.Name }).IsUnique();
    }
}
