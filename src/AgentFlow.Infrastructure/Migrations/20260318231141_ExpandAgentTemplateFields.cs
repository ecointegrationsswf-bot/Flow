using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAgentTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarName",
                table: "AgentTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseConditionKeyword",
                table: "AgentTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InactivityCloseHours",
                table: "AgentTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "AgentTemplates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LlmModel",
                table: "AgentTemplates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "AgentTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxTokens",
                table: "AgentTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetryIntervalHours",
                table: "AgentTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SendFrom",
                table: "AgentTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SendUntil",
                table: "AgentTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Temperature",
                table: "AgentTemplates",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Tone",
                table: "AgentTemplates",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarName",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "CloseConditionKeyword",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "InactivityCloseHours",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "LlmModel",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "MaxTokens",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "RetryIntervalHours",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "SendFrom",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "SendUntil",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "Temperature",
                table: "AgentTemplates");

            migrationBuilder.DropColumn(
                name: "Tone",
                table: "AgentTemplates");
        }
    }
}
