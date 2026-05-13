using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobExecutionItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledWebhookJobExecutionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContextType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContextId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContextLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledWebhookJobExecutionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledWebhookJobExecutionItems_ScheduledWebhookJobExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "ScheduledWebhookJobExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWebhookJobExecutionItems_ExecutionId_Status",
                table: "ScheduledWebhookJobExecutionItems",
                columns: new[] { "ExecutionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWebhookJobExecutionItems_TenantId",
                table: "ScheduledWebhookJobExecutionItems",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledWebhookJobExecutionItems");
        }
    }
}
