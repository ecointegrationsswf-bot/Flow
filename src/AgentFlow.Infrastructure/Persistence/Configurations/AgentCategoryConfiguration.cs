using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class AgentCategoryConfiguration : IEntityTypeConfiguration<AgentCategory>
{
    public void Configure(EntityTypeBuilder<AgentCategory> b)
    {
        b.HasKey(c => c.Id);
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(c => c.Name).IsUnique();
    }
}
