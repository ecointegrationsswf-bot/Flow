using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveSendGridToTenantAndPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SendGridApiKey",
                table: "CampaignTemplates");

            migrationBuilder.AddColumn<string>(
                name: "SendGridApiKey",
                table: "Tenants",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderEmail",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SendGridApiKey",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SenderEmail",
                table: "Tenants");

            migrationBuilder.AddColumn<string>(
                name: "SendGridApiKey",
                table: "CampaignTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
