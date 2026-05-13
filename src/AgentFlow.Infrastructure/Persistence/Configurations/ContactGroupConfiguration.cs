using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ContactGroupConfiguration : IEntityTypeConfiguration<ContactGroup>
{
    public void Configure(EntityTypeBuilder<ContactGroup> b)
    {
        b.ToTable("ContactGroups");
        b.HasKey(x => x.Id);

        b.HasIndex(x => new { x.ExecutionId, x.PhoneNormalized }).IsUnique();
        b.HasIndex(x => x.TenantId);

        b.Property(x => x.PhoneNormalized).HasMaxLength(20).IsRequired();
        b.Property(x => x.ClientName).HasMaxLength(300);
        b.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(ContactGroupStatus.Pending);

        b.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.NoAction);

        // Execution FK configurada desde DelinquencyExecution (Cascade)
        b.HasOne(x => x.Campaign)
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
