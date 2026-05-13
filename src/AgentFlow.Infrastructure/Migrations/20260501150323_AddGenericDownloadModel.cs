using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericDownloadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LogicalFieldKey",
                table: "ActionFieldMappings",
                newName: "ColumnKey");

            migrationBuilder.RenameIndex(
                name: "IX_ActionFieldMappings_ActionDefinitionId_LogicalFieldKey",
                table: "ActionFieldMappings",
                newName: "IX_ActionFieldMappings_ActionDefinitionId_ColumnKey");

            migrationBuilder.AddColumn<string>(
                name: "ExtractedDataJson",
                table: "DelinquencyItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyValue",
                table: "DelinquencyItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataType",
                table: "ActionFieldMappings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "string");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "ActionFieldMappings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "ActionFieldMappings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "RoleLabel",
                table: "ActionFieldMappings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ActionFieldMappings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ── Backfill: preservar mappings y datos existentes ───────────────
            // Las claves antiguas (PhoneNumber, ClientName, Amount, PolicyNumber) que vivían
            // en LogicalFieldCatalog ahora se traducen a Role + DisplayName por columna.
            migrationBuilder.Sql(@"
                UPDATE [ActionFieldMappings]
                   SET [Role]        = 'Phone',
                       [DisplayName] = 'Teléfono',
                       [DataType]    = 'phone',
                       [SortOrder]   = 0
                 WHERE [ColumnKey] = 'PhoneNumber';

                UPDATE [ActionFieldMappings]
                   SET [Role]        = 'ClientName',
                       [DisplayName] = 'Nombre del cliente',
                       [DataType]    = 'string',
                       [SortOrder]   = 1
                 WHERE [ColumnKey] = 'ClientName';

                UPDATE [ActionFieldMappings]
                   SET [Role]        = 'KeyValue',
                       [DisplayName] = 'Número de póliza',
                       [RoleLabel]   = 'Número de póliza',
                       [DataType]    = 'string',
                       [SortOrder]   = 2
                 WHERE [ColumnKey] = 'PolicyNumber';

                UPDATE [ActionFieldMappings]
                   SET [Role]        = 'Amount',
                       [DisplayName] = 'Saldo pendiente',
                       [DataType]    = 'currency',
                       [SortOrder]   = 3
                 WHERE [ColumnKey] = 'Amount';

                -- Backfill de DelinquencyItems: el viejo PolicyNumber semánticamente era el KeyValue.
                UPDATE [DelinquencyItems]
                   SET [KeyValue] = [PolicyNumber]
                 WHERE [PolicyNumber] IS NOT NULL AND [KeyValue] IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedDataJson",
                table: "DelinquencyItems");

            migrationBuilder.DropColumn(
                name: "KeyValue",
                table: "DelinquencyItems");

            migrationBuilder.DropColumn(
                name: "DataType",
                table: "ActionFieldMappings");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "ActionFieldMappings");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "ActionFieldMappings");

            migrationBuilder.DropColumn(
                name: "RoleLabel",
                table: "ActionFieldMappings");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ActionFieldMappings");

            migrationBuilder.RenameColumn(
                name: "ColumnKey",
                table: "ActionFieldMappings",
                newName: "LogicalFieldKey");

            migrationBuilder.RenameIndex(
                name: "IX_ActionFieldMappings_ActionDefinitionId_ColumnKey",
                table: "ActionFieldMappings",
                newName: "IX_ActionFieldMappings_ActionDefinitionId_LogicalFieldKey");
        }
    }
}
