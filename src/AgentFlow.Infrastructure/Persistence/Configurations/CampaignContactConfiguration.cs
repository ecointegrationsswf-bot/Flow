using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class CampaignContactConfiguration : IEntityTypeConfiguration<CampaignContact>
{
    public void Configure(EntityTypeBuilder<CampaignContact> b)
    {
        b.HasKey(c => c.Id);
        b.HasIndex(c => new { c.CampaignId, c.PhoneNumber });
        b.Property(c => c.PhoneNumber).HasMaxLength(20).IsRequired();
        b.Property(c => c.Email).HasMaxLength(300);
        b.Property(c => c.ClientName).HasMaxLength(300);
        b.Property(c => c.PolicyNumber).HasMaxLength(100);
        b.Property(c => c.InsuranceCompany).HasMaxLength(200);
        b.Property(c => c.PendingAmount).HasColumnType("decimal(18,2)");
        b.Property(c => c.Result).HasConversion<string>().HasMaxLength(50);
        b.Property(c => c.ExtraData).HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new()
        ).HasColumnType("nvarchar(max)");
        b.Property(c => c.ContactDataJson).HasColumnType("nvarchar(max)");
        b.Property(c => c.DispatchStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(c => c.GeneratedMessage).HasColumnType("nvarchar(max)");
        b.Property(c => c.ExternalMessageId).HasMaxLength(200);
        b.Property(c => c.DispatchError).HasMaxLength(2000);
        b.Property(c => c.FollowUpsSentJson).HasMaxLength(200).HasDefaultValueSql("'[]'");
        b.HasIndex(c => new { c.CampaignId, c.DispatchStatus, c.ClaimedAt });

        // Índice para el query del CampaignWorker:
        // SELECT contactos donde DispatchStatus IN (Queued, Retry) y (ScheduledFor IS NULL OR ScheduledFor <= now).
        b.HasIndex(c => new { c.DispatchStatus, c.ScheduledFor });
    }
}
