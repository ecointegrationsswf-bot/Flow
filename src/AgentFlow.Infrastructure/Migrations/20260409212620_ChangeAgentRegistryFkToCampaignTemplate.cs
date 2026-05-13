using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeAgentRegistryFkToCampaignTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentRegistryEntries_AgentDefinitions_AgentDefinitionId",
                table: "AgentRegistryEntries");

            migrationBuilder.RenameColumn(
                name: "AgentDefinitionId",
                table: "AgentRegistryEntries",
                newName: "CampaignTemplateId");

            migrationBuilder.RenameIndex(
                name: "IX_AgentRegistryEntries_AgentDefinitionId",
                table: "AgentRegistryEntries",
                newName: "IX_AgentRegistryEntries_CampaignTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRegistryEntries_TenantId_CampaignTemplateId",
                table: "AgentRegistryEntries",
                columns: new[] { "TenantId", "CampaignTemplateId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentRegistryEntries_CampaignTemplates_CampaignTemplateId",
                table: "AgentRegistryEntries",
                column: "CampaignTemplateId",
                principalTable: "CampaignTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentRegistryEntries_CampaignTemplates_CampaignTemplateId",
                table: "AgentRegistryEntries");

            migrationBuilder.DropIndex(
                name: "IX_AgentRegistryEntries_TenantId_CampaignTemplateId",
                table: "AgentRegistryEntries");

            migrationBuilder.RenameColumn(
                name: "CampaignTemplateId",
                table: "AgentRegistryEntries",
                newName: "AgentDefinitionId");

            migrationBuilder.RenameIndex(
                name: "IX_AgentRegistryEntries_CampaignTemplateId",
                table: "AgentRegistryEntries",
                newName: "IX_AgentRegistryEntries_AgentDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentRegistryEntries_AgentDefinitions_AgentDefinitionId",
                table: "AgentRegistryEntries",
                column: "AgentDefinitionId",
                principalTable: "AgentDefinitions",
                principalColumn: "Id");
        }
    }
}
