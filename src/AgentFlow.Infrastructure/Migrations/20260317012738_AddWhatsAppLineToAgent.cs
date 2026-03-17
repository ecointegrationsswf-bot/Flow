using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppLineToAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppLineId",
                table: "AgentDefinitions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentDefinitions_WhatsAppLineId",
                table: "AgentDefinitions",
                column: "WhatsAppLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentDefinitions_WhatsAppLines_WhatsAppLineId",
                table: "AgentDefinitions",
                column: "WhatsAppLineId",
                principalTable: "WhatsAppLines",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentDefinitions_WhatsAppLines_WhatsAppLineId",
                table: "AgentDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_AgentDefinitions_WhatsAppLineId",
                table: "AgentDefinitions");

            migrationBuilder.DropColumn(
                name: "WhatsAppLineId",
                table: "AgentDefinitions");
        }
    }
}
