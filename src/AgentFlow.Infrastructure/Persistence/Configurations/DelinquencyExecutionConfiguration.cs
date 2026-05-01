using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class DelinquencyExecutionConfiguration : IEntityTypeConfiguration<DelinquencyExecution>
{
    public void Configure(EntityTypeBuilder<DelinquencyExecution> b)
    {
        b.ToTable("DelinquencyExecutions");
        b.HasKey(x => x.Id);

        b.HasIndex(x => new { x.TenantId, x.StartedAt });
        b.HasIndex(x => x.ActionDefinitionId);

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(DelinquencyExecutionStatus.Pending);
        b.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");

        b.HasOne(x => x.ActionDefinition)
            .WithMany()
            .HasForeignKey(x => x.ActionDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Items)
            .WithOne(i => i.Execution)
            .HasForeignKey(i => i.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Groups)
            .WithOne(g => g.Execution)
            .HasForeignKey(g => g.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
