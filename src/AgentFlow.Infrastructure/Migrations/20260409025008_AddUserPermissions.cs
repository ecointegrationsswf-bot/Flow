using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AttentionStartTime",
                table: "CampaignTemplates",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValueSql: "'08:00'",
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldDefaultValue: "08:00");

            migrationBuilder.AlterColumn<string>(
                name: "AttentionEndTime",
                table: "CampaignTemplates",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValueSql: "'17:00'",
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldDefaultValue: "17:00");

            migrationBuilder.AlterColumn<string>(
                name: "AttentionDays",
                table: "CampaignTemplates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValueSql: "'[1,2,3,4,5]'",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldDefaultValue: "[1,2,3,4,5]");

            migrationBuilder.AddColumn<string>(
                name: "Permissions",
                table: "AppUsers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "AttentionStartTime",
                table: "CampaignTemplates",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "08:00",
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldDefaultValueSql: "'08:00'");

            migrationBuilder.AlterColumn<string>(
                name: "AttentionEndTime",
                table: "CampaignTemplates",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "17:00",
                oldClrType: typeof(string),
                oldType: "nvarchar(5)",
                oldMaxLength: 5,
                oldDefaultValueSql: "'17:00'");

            migrationBuilder.AlterColumn<string>(
                name: "AttentionDays",
                table: "CampaignTemplates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "[1,2,3,4,5]",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldDefaultValueSql: "'[1,2,3,4,5]'");
        }
    }
}
