using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeWhatsAppLineTenantOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppLines_TenantId_InstanceId",
                table: "WhatsAppLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "WhatsAppLines",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppLines_TenantId_InstanceId",
                table: "WhatsAppLines",
                columns: new[] { "TenantId", "InstanceId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppLines_TenantId_InstanceId",
                table: "WhatsAppLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "WhatsAppLines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppLines_TenantId_InstanceId",
                table: "WhatsAppLines",
                columns: new[] { "TenantId", "InstanceId" },
                unique: true);
        }
    }
}
