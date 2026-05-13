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
        // TenantId nullable: NULL = acción global; no-NULL = acción legacy scopada.
        b.Property(a => a.TenantId).IsRequired(false);
        b.HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.SetNull);
        b.Property(a => a.Name).HasMaxLength(100).IsRequired();
        b.Property(a => a.Description).HasMaxLength(500);
        b.Property(a => a.WebhookUrl).HasMaxLength(500);
        b.Property(a => a.WebhookMethod).HasMaxLength(10);

        // Taxonomía del Webhook Contract System — enums como string para legibilidad en BD
        b.Property(a => a.ExecutionMode).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(Domain.Enums.ExecutionMode.FireAndForget);
        b.Property(a => a.ParamSource).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(Domain.Enums.ParamSource.SystemOnly);
        b.Property(a => a.ConversationImpact).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(Domain.Enums.ConversationImpact.Transparent);
        b.Property(a => a.RequiredParams).HasColumnType("nvarchar(max)");
        b.Property(a => a.ScheduleConfig).HasColumnType("nvarchar(max)");

        b.HasIndex(a => new { a.TenantId, a.Name }).IsUnique();
    }
}
