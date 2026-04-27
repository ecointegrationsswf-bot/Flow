using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ScheduledWebhookJobExecutionConfiguration : IEntityTypeConfiguration<ScheduledWebhookJobExecution>
{
    public void Configure(EntityTypeBuilder<ScheduledWebhookJobExecution> b)
    {
        b.ToTable("ScheduledWebhookJobExecutions");
        b.HasKey(e => e.Id);

        b.HasOne(e => e.Job)
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Property(e => e.Status).HasMaxLength(20).IsRequired();
        b.Property(e => e.TriggeredBy).HasMaxLength(50);
        b.Property(e => e.ContextId).HasMaxLength(200);
        b.Property(e => e.ErrorDetail).HasColumnType("nvarchar(max)");

        // Búsquedas frecuentes desde la UI: historial por job ordenado desc.
        b.HasIndex(e => new { e.JobId, e.StartedAt });
    }
}
