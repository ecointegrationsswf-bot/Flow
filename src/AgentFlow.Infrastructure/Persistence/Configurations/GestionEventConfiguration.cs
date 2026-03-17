using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class GestionEventConfiguration : IEntityTypeConfiguration<GestionEvent>
{
    public void Configure(EntityTypeBuilder<GestionEvent> b)
    {
        b.HasKey(g => g.Id);
        b.HasIndex(g => g.ConversationId);
        b.Property(g => g.Result).HasConversion<string>().HasMaxLength(50);
        b.Property(g => g.Origin).HasMaxLength(100).IsRequired();
        b.Property(g => g.Notes).HasColumnType("nvarchar(max)");
    }
}
