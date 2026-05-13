using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadWebhookToDelinquencyConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DownloadWebhookHeaders",
                table: "ActionDelinquencyConfigs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadWebhookMethod",
                table: "ActionDelinquencyConfigs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "GET");

            migrationBuilder.AddColumn<string>(
                name: "DownloadWebhookUrl",
                table: "ActionDelinquencyConfigs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownloadWebhookHeaders",
                table: "ActionDelinquencyConfigs");

            migrationBuilder.DropColumn(
                name: "DownloadWebhookMethod",
                table: "ActionDelinquencyConfigs");

            migrationBuilder.DropColumn(
                name: "DownloadWebhookUrl",
                table: "ActionDelinquencyConfigs");
        }
    }
}
