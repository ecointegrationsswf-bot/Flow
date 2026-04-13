using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultWebhookContractToActionDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultTriggerConfig",
                table: "ActionDefinitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultWebhookContract",
                table: "ActionDefinitions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultTriggerConfig",
                table: "ActionDefinitions");

            migrationBuilder.DropColumn(
                name: "DefaultWebhookContract",
                table: "ActionDefinitions");
        }
    }
}
