using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class AgentTemplateConfiguration : IEntityTypeConfiguration<AgentTemplate>
{
    public void Configure(EntityTypeBuilder<AgentTemplate> b)
    {
        b.HasKey(t => t.Id);
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.Category).HasMaxLength(100).IsRequired();
        b.Property(t => t.SystemPrompt).HasColumnType("nvarchar(max)").IsRequired();
        b.HasIndex(t => t.Category);
    }
}
