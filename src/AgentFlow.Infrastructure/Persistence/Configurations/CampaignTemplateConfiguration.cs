using System.Text.Json;
using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class CampaignTemplateConfiguration : IEntityTypeConfiguration<CampaignTemplate>
{
    public void Configure(EntityTypeBuilder<CampaignTemplate> b)
    {
        b.HasKey(t => t.Id);
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.EmailAddress).HasMaxLength(200);

        b.Property(t => t.FollowUpHours)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            ).HasMaxLength(500);

        b.Property(t => t.LabelIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
            ).HasMaxLength(2000);

        // Acciones y Prompts vinculados
        b.Property(t => t.ActionIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
            ).HasMaxLength(2000);

        b.Property(t => t.PromptTemplateIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
            ).HasMaxLength(2000);

        b.Property(t => t.AttentionDays)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int> { 1, 2, 3, 4, 5 }
            ).HasMaxLength(100).HasDefaultValueSql("'[1,2,3,4,5]'");
        b.Property(t => t.AttentionStartTime).HasMaxLength(5).HasDefaultValueSql("'08:00'");
        b.Property(t => t.AttentionEndTime).HasMaxLength(5).HasDefaultValueSql("'17:00'");

        b.Property(t => t.ActionConfigs).HasColumnType("nvarchar(max)");
        b.Property(t => t.OutOfContextPolicy).HasConversion<string>().HasMaxLength(20).HasDefaultValue(Domain.Enums.OutOfContextPolicy.Contain);

        b.HasOne(t => t.Tenant).WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(t => t.AgentDefinition).WithMany().HasForeignKey(t => t.AgentDefinitionId).OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(t => t.TenantId);
    }
}
