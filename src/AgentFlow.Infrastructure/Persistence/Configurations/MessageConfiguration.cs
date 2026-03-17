using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.HasKey(m => m.Id);
        b.HasIndex(m => m.ConversationId);
        b.Property(m => m.Direction).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Content).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(m => m.ExternalMessageId).HasMaxLength(200);
        b.Property(m => m.AgentName).HasMaxLength(200);
        b.Property(m => m.DetectedIntent).HasMaxLength(50);
    }
}
