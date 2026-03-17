using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class AgentDefinitionConfiguration : IEntityTypeConfiguration<AgentDefinition>
{
    public void Configure(EntityTypeBuilder<AgentDefinition> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.Name).HasMaxLength(200).IsRequired();
        b.Property(a => a.Type).HasConversion<string>();
        b.Property(a => a.Language).HasMaxLength(10);
        b.Property(a => a.LlmModel).HasMaxLength(100);
        b.Property(a => a.SystemPrompt).HasColumnType("nvarchar(max)");
        b.Property(a => a.EnabledChannels)
            .HasConversion(
                v => string.Join(',', v.Select(c => c.ToString())),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(Enum.Parse<ChannelType>).ToList()
            ).HasMaxLength(100);

        b.HasOne(a => a.WhatsAppLine)
            .WithMany()
            .HasForeignKey(a => a.WhatsAppLineId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
