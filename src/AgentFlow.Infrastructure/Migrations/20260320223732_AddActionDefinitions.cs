using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActionDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequiresWebhook = table.Column<bool>(type: "bit", nullable: false),
                    SendsEmail = table.Column<bool>(type: "bit", nullable: false),
                    SendsSms = table.Column<bool>(type: "bit", nullable: false),
                    WebhookUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WebhookMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionDefinitions_Name",
                table: "ActionDefinitions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionDefinitions");
        }
    }
}
