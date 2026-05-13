using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttentionScheduleToCampaignTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttentionDays",
                table: "CampaignTemplates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "[1,2,3,4,5]");

            migrationBuilder.AddColumn<string>(
                name: "AttentionEndTime",
                table: "CampaignTemplates",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "17:00");

            migrationBuilder.AddColumn<string>(
                name: "AttentionStartTime",
                table: "CampaignTemplates",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "08:00");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttentionDays",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "AttentionEndTime",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "AttentionStartTime",
                table: "CampaignTemplates");
        }
    }
}
