using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDelinquencyDownloadFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDelinquencyDownload",
                table: "ActionDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Backfill: la acción legacy "DOWNLOAD_DELINQUENCY_DATA" ya cumple este rol —
            // la marcamos para que el flujo siga funcionando tras este cambio.
            migrationBuilder.Sql(
                "UPDATE [ActionDefinitions] SET [IsDelinquencyDownload] = 1 WHERE [Name] = 'DOWNLOAD_DELINQUENCY_DATA';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDelinquencyDownload",
                table: "ActionDefinitions");
        }
    }
}
