using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignTemplateAgentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseConditionKeyword",
                table: "CampaignTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InactivityCloseHours",
                table: "CampaignTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "CampaignTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxTokens",
                table: "CampaignTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetryIntervalHours",
                table: "CampaignTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SendFrom",
                table: "CampaignTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SendUntil",
                table: "CampaignTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemPrompt",
                table: "CampaignTemplates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseConditionKeyword",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "InactivityCloseHours",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "MaxTokens",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "RetryIntervalHours",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "SendFrom",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "SendUntil",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "SystemPrompt",
                table: "CampaignTemplates");
        }
    }
}
