using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class WebhookDispatchLogConfiguration : IEntityTypeConfiguration<WebhookDispatchLog>
{
    public void Configure(EntityTypeBuilder<WebhookDispatchLog> b)
    {
        b.ToTable("WebhookDispatchLogs");
        b.HasKey(e => e.Id);

        b.Property(e => e.ActionSlug).HasMaxLength(100).IsRequired();
        b.Property(e => e.TargetUrl).HasMaxLength(2000).IsRequired();
        b.Property(e => e.HttpMethod).HasMaxLength(10).IsRequired();
        b.Property(e => e.RequestContentType).HasMaxLength(100);
        b.Property(e => e.ClientPhone).HasMaxLength(20);
        b.Property(e => e.Status).HasMaxLength(20).IsRequired();

        b.Property(e => e.RequestPayloadJson).HasColumnType("nvarchar(max)");
        b.Property(e => e.ResponseBody).HasColumnType("nvarchar(max)");
        b.Property(e => e.ErrorMessage).HasColumnType("nvarchar(max)");

        // Búsquedas frecuentes desde la UI/API.
        b.HasIndex(e => new { e.TenantId, e.StartedAt });
        b.HasIndex(e => e.JobExecutionId);
        b.HasIndex(e => e.ConversationId);
        b.HasIndex(e => e.ClientPhone);
        b.HasIndex(e => new { e.ActionSlug, e.StartedAt });
    }
}
