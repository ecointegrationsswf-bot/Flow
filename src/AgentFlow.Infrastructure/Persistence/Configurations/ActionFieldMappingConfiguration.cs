using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class ActionFieldMappingConfiguration : IEntityTypeConfiguration<ActionFieldMapping>
{
    public void Configure(EntityTypeBuilder<ActionFieldMapping> b)
    {
        b.ToTable("ActionFieldMappings");
        b.HasKey(x => x.Id);

        // Un mapping único por (ActionDefinitionId, ColumnKey)
        b.HasIndex(x => new { x.ActionDefinitionId, x.ColumnKey }).IsUnique();

        b.Property(x => x.ColumnKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.JsonPath).HasMaxLength(500).IsRequired();
        b.Property(x => x.RoleLabel).HasMaxLength(200);
        b.Property(x => x.DataType).HasMaxLength(20).IsRequired().HasDefaultValue("string");
        b.Property(x => x.DefaultValue).HasMaxLength(500);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(FieldRole.None);

        b.HasOne(x => x.ActionDefinition)
            .WithMany()
            .HasForeignKey(x => x.ActionDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
