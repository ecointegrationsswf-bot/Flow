using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignFollowUpAndAutoClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScheduleConfig",
                table: "ActionDefinitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScheduledWebhookJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CronExpression = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TriggerEvent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DelayMinutes = table.Column<int>(type: "int", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "AllTenants"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LastRunSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledWebhookJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledWebhookJobs_ActionDefinitions_ActionDefinitionId",
                        column: x => x.ActionDefinitionId,
                        principalTable: "ActionDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledWebhookJobExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TotalRecords = table.Column<int>(type: "int", nullable: false),
                    SuccessCount = table.Column<int>(type: "int", nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    ErrorDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TriggeredBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ContextId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledWebhookJobExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledWebhookJobExecutions_ScheduledWebhookJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ScheduledWebhookJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWebhookJobExecutions_JobId_StartedAt",
                table: "ScheduledWebhookJobExecutions",
                columns: new[] { "JobId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWebhookJobs_ActionDefinitionId",
                table: "ScheduledWebhookJobs",
                column: "ActionDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWebhookJobs_IsActive_NextRunAt",
                table: "ScheduledWebhookJobs",
                columns: new[] { "IsActive", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWebhookJobs_TriggerEvent",
                table: "ScheduledWebhookJobs",
                column: "TriggerEvent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledWebhookJobExecutions");

            migrationBuilder.DropTable(
                name: "ScheduledWebhookJobs");

            migrationBuilder.DropColumn(
                name: "ScheduleConfig",
                table: "ActionDefinitions");
        }
    }
}
