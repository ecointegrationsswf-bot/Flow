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
    }
}
