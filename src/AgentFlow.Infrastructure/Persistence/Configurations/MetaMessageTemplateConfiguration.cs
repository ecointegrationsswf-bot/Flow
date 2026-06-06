using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class MetaMessageTemplateConfiguration : IEntityTypeConfiguration<MetaMessageTemplate>
{
    public void Configure(EntityTypeBuilder<MetaMessageTemplate> b)
    {
        b.HasKey(t => t.Id);

        b.Property(t => t.Name).HasMaxLength(512).IsRequired();
        b.Property(t => t.Language).HasMaxLength(20).IsRequired();
        b.Property(t => t.Category).HasMaxLength(40).IsRequired();
        b.Property(t => t.HeaderType).HasMaxLength(20);
        b.Property(t => t.HeaderText).HasMaxLength(200);
        b.Property(t => t.BodyText);                 // nvarchar(max)
        b.Property(t => t.FooterText).HasMaxLength(120);
        b.Property(t => t.VariableSamplesJson);      // nvarchar(max)
        b.Property(t => t.MetaTemplateId).HasMaxLength(100);
        b.Property(t => t.MetaStatus).HasMaxLength(40).IsRequired();
        b.Property(t => t.Purpose).HasMaxLength(20).IsRequired();
        b.Property(t => t.MetaRejectedReason).HasMaxLength(1000);
        b.Property(t => t.ParameterMappingJson);     // nvarchar(max) — Fase 2

        b.HasOne(t => t.WhatsAppLine)
            .WithMany()
            .HasForeignKey(t => t.WhatsAppLineId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(t => t.TenantId);
        b.HasIndex(t => t.WhatsAppLineId);
        b.HasIndex(t => t.BubbleGroupId);
        b.HasIndex(t => t.CampaignTemplateId);
        // Meta no permite duplicar nombre+idioma dentro de un mismo WABA/línea.
        b.HasIndex(t => new { t.WhatsAppLineId, t.Name, t.Language }).IsUnique();
    }
}
