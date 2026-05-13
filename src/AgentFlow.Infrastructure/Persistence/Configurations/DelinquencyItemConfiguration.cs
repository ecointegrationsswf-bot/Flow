using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class DelinquencyItemConfiguration : IEntityTypeConfiguration<DelinquencyItem>
{
    public void Configure(EntityTypeBuilder<DelinquencyItem> b)
    {
        b.ToTable("DelinquencyItems");
        b.HasKey(x => x.Id);

        b.HasIndex(x => x.ExecutionId);
        b.HasIndex(x => x.PhoneNormalized);

        b.Property(x => x.PhoneRaw).HasMaxLength(50);
        b.Property(x => x.PhoneNormalized).HasMaxLength(20);
        b.Property(x => x.ClientName).HasMaxLength(300);
        b.Property(x => x.PolicyNumber).HasMaxLength(100);
        b.Property(x => x.KeyValue).HasMaxLength(200);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.RawData).HasColumnType("nvarchar(max)");
        b.Property(x => x.ExtractedDataJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.DiscardReason).HasMaxLength(300);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(DelinquencyItemStatus.Pending);

        // Execution FK configurada desde DelinquencyExecution (Cascade)
        b.HasOne(x => x.Group)
            .WithMany(g => g.Items)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
