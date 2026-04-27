using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationLabelingAndResultWebhook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoCloseMessage",
                table: "CampaignTemplates",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUpMessagesJson",
                table: "CampaignTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUpsSentJson",
                table: "CampaignContacts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                defaultValueSql: "'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoCloseMessage",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "FollowUpMessagesJson",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "FollowUpsSentJson",
                table: "CampaignContacts");
        }
    }
}
