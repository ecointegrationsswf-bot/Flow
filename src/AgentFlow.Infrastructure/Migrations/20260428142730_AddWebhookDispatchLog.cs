using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookDispatchLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookDispatchLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    JobExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActionDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActionSlug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RequestContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDispatchLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDispatchLogs_ActionSlug_StartedAt",
                table: "WebhookDispatchLogs",
                columns: new[] { "ActionSlug", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDispatchLogs_ClientPhone",
                table: "WebhookDispatchLogs",
                column: "ClientPhone");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDispatchLogs_ConversationId",
                table: "WebhookDispatchLogs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDispatchLogs_JobExecutionId",
                table: "WebhookDispatchLogs",
                column: "JobExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDispatchLogs_TenantId_StartedAt",
                table: "WebhookDispatchLogs",
                columns: new[] { "TenantId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDispatchLogs");
        }
    }
}
