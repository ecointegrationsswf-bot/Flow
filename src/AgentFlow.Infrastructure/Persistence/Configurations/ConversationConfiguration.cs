using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.HasKey(c => c.Id);
        b.HasIndex(c => new { c.TenantId, c.ClientPhone, c.Status });
        b.Property(c => c.ClientPhone).HasMaxLength(20).IsRequired();
        b.Property(c => c.Channel).HasConversion<string>();
        b.Property(c => c.Status).HasConversion<string>();
        b.Property(c => c.GestionResult).HasConversion<string>();
        b.HasMany(c => c.Messages).WithOne(m => m.Conversation).HasForeignKey(m => m.ConversationId);
        b.HasMany(c => c.GestionEvents).WithOne(g => g.Conversation).HasForeignKey(g => g.ConversationId);

        // Fase 3 — relación con ConversationLabel + índice para queries del LabelingJob.
        b.HasOne(c => c.Label)
            .WithMany()
            .HasForeignKey(c => c.LabelId)
            // NoAction porque ConversationLabel tiene FK al Tenant que cascadea a Conversations,
            // y SetNull crearía multiple cascade paths. Si se borra una label se queda
            // referenciada huérfana — la limpieza es responsabilidad del CRUD de labels.
            .OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(c => new { c.TenantId, c.Status, c.LabelId });
    }
}
