using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceDocumentsEnabledToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReferenceDocumentsEnabled",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Backfill: los tenants que ya tienen documentos adjuntos venían usando la
            // feature implícitamente — se activa para no regresionar su comportamiento.
            migrationBuilder.Sql(@"
                UPDATE Tenants
                SET ReferenceDocumentsEnabled = 1
                WHERE EXISTS (
                    SELECT 1 FROM CampaignTemplateDocuments d WHERE d.TenantId = Tenants.Id
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceDocumentsEnabled",
                table: "Tenants");
        }
    }
}
