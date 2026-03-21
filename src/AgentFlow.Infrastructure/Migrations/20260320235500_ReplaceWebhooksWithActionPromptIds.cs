using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceWebhooksWithActionPromptIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop webhook columns
            migrationBuilder.DropColumn(
                name: "WebhookFieldMappings",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "WebhookReceiveEnabled",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "WebhookReceiveSecret",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "WebhookReceiveUrl",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "WebhookSendEnabled",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "WebhookSendHeaders",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "WebhookSendMethod",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "WebhookSendUrl",
                table: "CampaignTemplates");

            // Add action and prompt template ID columns
            migrationBuilder.AddColumn<string>(
                name: "ActionIds",
                table: "CampaignTemplates",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "PromptTemplateIds",
                table: "CampaignTemplates",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActionIds",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "PromptTemplateIds",
                table: "CampaignTemplates");

            // Restore webhook columns
            migrationBuilder.AddColumn<string>(
                name: "WebhookFieldMappings",
                table: "CampaignTemplates",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "WebhookReceiveEnabled",
                table: "CampaignTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WebhookReceiveSecret",
                table: "CampaignTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookReceiveUrl",
                table: "CampaignTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WebhookSendEnabled",
                table: "CampaignTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSendHeaders",
                table: "CampaignTemplates",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebhookSendMethod",
                table: "CampaignTemplates",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSendUrl",
                table: "CampaignTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
