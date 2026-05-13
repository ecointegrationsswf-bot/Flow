using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class LogicalFieldCatalogConfiguration : IEntityTypeConfiguration<LogicalFieldCatalog>
{
    public void Configure(EntityTypeBuilder<LogicalFieldCatalog> b)
    {
        b.ToTable("LogicalFieldCatalog");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.DataType).HasMaxLength(50).HasDefaultValue("string");
        b.Property(x => x.SortOrder).HasDefaultValue(0);

        // Seed del catálogo de campos estándar
        b.HasData(
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000001"), Key = "PhoneNumber",   DisplayName = "Número de Teléfono",  DataType = "phone",  IsRequired = true,  SortOrder = 1 },
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000002"), Key = "ClientName",    DisplayName = "Nombre del Cliente",  DataType = "string", IsRequired = false, SortOrder = 2 },
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000003"), Key = "PolicyNumber",  DisplayName = "Número de Póliza",    DataType = "string", IsRequired = false, SortOrder = 3 },
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000004"), Key = "Amount",        DisplayName = "Monto en Mora",       DataType = "number", IsRequired = false, SortOrder = 4 },
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000005"), Key = "Insurer",       DisplayName = "Aseguradora",         DataType = "string", IsRequired = false, SortOrder = 5 },
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000006"), Key = "DueDate",       DisplayName = "Fecha de Vencimiento",DataType = "string", IsRequired = false, SortOrder = 6 },
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000007"), Key = "PolicyType",    DisplayName = "Tipo de Póliza",      DataType = "string", IsRequired = false, SortOrder = 7 },
            new LogicalFieldCatalog { Id = new Guid("11111111-0000-0000-0000-000000000008"), Key = "ExecutiveEmail",DisplayName = "Email del Ejecutivo", DataType = "string", IsRequired = false, SortOrder = 8 }
        );
    }
}
