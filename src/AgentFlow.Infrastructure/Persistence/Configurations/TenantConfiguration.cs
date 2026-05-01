using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

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
        b.Property(t => t.Country).HasMaxLength(100);
        b.Property(t => t.MonthlyBillingAmount).HasColumnType("decimal(18,2)");
        b.Property(t => t.WhatsAppProvider).HasConversion<string>();
        b.Property(t => t.LlmProvider).HasConversion<string>().HasMaxLength(50);
        b.Property(t => t.LlmApiKey).HasMaxLength(500);
        b.Property(t => t.LlmModel).HasMaxLength(100);
        b.Property(t => t.SendGridApiKey).HasMaxLength(500);
        b.Property(t => t.SenderEmail).HasMaxLength(200);
        b.Property(t => t.CampaignMessageDelaySeconds).HasDefaultValue(10);

        // AssignedPromptIds: JSON serializado. Si está vacío el tenant ve todos los prompts (retrocompat).
        b.Property(t => t.AssignedPromptIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<Guid>()
                    : JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
            ).HasColumnType("nvarchar(max)");

        // AssignedActionIds: mismo patrón que AssignedPromptIds.
        b.Property(t => t.AssignedActionIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<Guid>()
                    : JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
            ).HasColumnType("nvarchar(max)");

        b.Property(t => t.LabelingAnalysisPrompt).HasColumnType("nvarchar(max)");
        b.Property(t => t.LabelingResultSchemaPrompt).HasColumnType("nvarchar(max)");
        b.Property(t => t.CodigoPaisDefault).HasMaxLength(10).HasDefaultValue("507");

        b.HasMany(t => t.Agents).WithOne(a => a.Tenant).HasForeignKey(a => a.TenantId);
        b.HasMany(t => t.Campaigns).WithOne(c => c.Tenant).HasForeignKey(c => c.TenantId);
    }
}
