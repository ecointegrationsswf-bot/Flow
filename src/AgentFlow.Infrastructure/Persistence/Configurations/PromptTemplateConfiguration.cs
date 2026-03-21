using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> b)
    {
        b.ToTable("PromptTemplates");
        b.HasKey(p => p.Id);
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Description).HasMaxLength(500);
        b.Property(p => p.SystemPrompt).HasColumnType("nvarchar(max)");
        b.Property(p => p.ResultPrompt).HasColumnType("nvarchar(max)");
        b.Property(p => p.AnalysisPrompts).HasColumnType("nvarchar(max)");
        b.Property(p => p.FieldMapping).HasColumnType("nvarchar(max)");
        b.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}
