using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ConversationLabelConfiguration : IEntityTypeConfiguration<ConversationLabel>
{
    public void Configure(EntityTypeBuilder<ConversationLabel> b)
    {
        b.HasKey(l => l.Id);
        b.Property(l => l.Name).HasMaxLength(100).IsRequired();
        b.Property(l => l.Color).HasMaxLength(10).IsRequired();
        b.Property(l => l.Keywords).HasConversion(
            v => string.Join("||", v),
            v => v.Split("||", StringSplitOptions.RemoveEmptyEntries).ToList()
        );
        b.HasIndex(l => new { l.TenantId, l.Name }).IsUnique();
        b.HasOne(l => l.Tenant).WithMany().HasForeignKey(l => l.TenantId);
    }
}
