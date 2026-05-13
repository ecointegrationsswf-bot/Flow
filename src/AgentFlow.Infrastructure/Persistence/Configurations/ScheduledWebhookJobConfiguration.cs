using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ScheduledWebhookJobConfiguration : IEntityTypeConfiguration<ScheduledWebhookJob>
{
    public void Configure(EntityTypeBuilder<ScheduledWebhookJob> b)
    {
        b.ToTable("ScheduledWebhookJobs");
        b.HasKey(j => j.Id);

        b.HasOne(j => j.ActionDefinition)
            .WithMany()
            .HasForeignKey(j => j.ActionDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(j => j.TriggerType).HasMaxLength(20).IsRequired();
        b.Property(j => j.CronExpression).HasMaxLength(100);
        b.Property(j => j.TriggerEvent).HasMaxLength(100);
        b.Property(j => j.Scope).HasMaxLength(20).IsRequired().HasDefaultValue("AllTenants");
        b.Property(j => j.LastRunStatus).HasMaxLength(20);
        b.Property(j => j.LastRunSummary).HasMaxLength(1000);

        // Índice de Worker: solo jobs activos con NextRunAt vencido.
        b.HasIndex(j => new { j.IsActive, j.NextRunAt });
        b.HasIndex(j => j.TriggerEvent);
    }
}
