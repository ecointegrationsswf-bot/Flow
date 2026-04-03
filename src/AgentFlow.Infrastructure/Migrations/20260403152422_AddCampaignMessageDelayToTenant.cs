using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignMessageDelayToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CampaignMessageDelaySeconds",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<DateTime>(
                name: "LaunchedAt",
                table: "Campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LaunchedByUserId",
                table: "Campaigns",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Campaigns",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "CampaignContacts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactDataJson",
                table: "CampaignContacts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DispatchAttempts",
                table: "CampaignContacts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DispatchError",
                table: "CampaignContacts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchStatus",
                table: "CampaignContacts",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalMessageId",
                table: "CampaignContacts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeneratedMessage",
                table: "CampaignContacts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "CampaignContacts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AvatarUrl",
                table: "AppUsers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignDispatchLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    PromptSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactDataSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneratedMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UltraMsgResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorDetail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignDispatchLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignContacts_CampaignId_DispatchStatus_ClaimedAt",
                table: "CampaignContacts",
                columns: new[] { "CampaignId", "DispatchStatus", "ClaimedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDispatchLogs_CampaignContactId",
                table: "CampaignDispatchLogs",
                column: "CampaignContactId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDispatchLogs_CampaignId_OccurredAt",
                table: "CampaignDispatchLogs",
                columns: new[] { "CampaignId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignDispatchLogs");

            migrationBuilder.DropIndex(
                name: "IX_CampaignContacts_CampaignId_DispatchStatus_ClaimedAt",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "CampaignMessageDelaySeconds",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LaunchedAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "LaunchedByUserId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "ContactDataJson",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "DispatchAttempts",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "DispatchError",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "DispatchStatus",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "ExternalMessageId",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "GeneratedMessage",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "CampaignContacts");

            migrationBuilder.AlterColumn<string>(
                name: "AvatarUrl",
                table: "AppUsers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
