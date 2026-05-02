using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ScheduledWebhookJobExecutionItemConfiguration
    : IEntityTypeConfiguration<ScheduledWebhookJobExecutionItem>
{
    public void Configure(EntityTypeBuilder<ScheduledWebhookJobExecutionItem> b)
    {
        b.ToTable("ScheduledWebhookJobExecutionItems");
        b.HasKey(x => x.Id);

        b.Property(x => x.ExecutionId).IsRequired();
        b.HasOne(x => x.Execution)
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.TenantId).IsRequired(false);
        b.Property(x => x.ContextType).HasMaxLength(30).IsRequired();
        b.Property(x => x.ContextId).HasMaxLength(200);
        b.Property(x => x.ContextLabel).HasMaxLength(300);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");
        b.Property(x => x.DurationMs);
        b.Property(x => x.CreatedAt).IsRequired();

        b.HasIndex(x => new { x.ExecutionId, x.Status });
        b.HasIndex(x => x.TenantId);
    }
}
