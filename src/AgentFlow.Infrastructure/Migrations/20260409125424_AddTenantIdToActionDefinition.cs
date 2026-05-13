using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToActionDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActionDefinitions_Name",
                table: "ActionDefinitions");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ActionDefinitions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Asociar ActionDefinitions existentes al tenant que las usa en CampaignTemplates.
            // Si una acción no está referenciada en ningún maestro, se asigna al primer tenant activo.
            migrationBuilder.Sql(@"
                -- Paso 1: asignar al tenant del CampaignTemplate que referencia la acción
                UPDATE ad
                SET ad.TenantId = ct.TenantId
                FROM ActionDefinitions ad
                CROSS APPLY (
                    SELECT TOP 1 ct2.TenantId
                    FROM CampaignTemplates ct2
                    WHERE ct2.ActionIds LIKE '%' + CAST(ad.Id AS NVARCHAR(36)) + '%'
                ) ct
                WHERE ad.TenantId = '00000000-0000-0000-0000-000000000000';

                -- Paso 2: las que no matchearon, asignar al primer tenant activo
                UPDATE ActionDefinitions
                SET TenantId = (SELECT TOP 1 Id FROM Tenants WHERE IsActive = 1 ORDER BY CreatedAt)
                WHERE TenantId = '00000000-0000-0000-0000-000000000000';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_ActionDefinitions_TenantId_Name",
                table: "ActionDefinitions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ActionDefinitions_Tenants_TenantId",
                table: "ActionDefinitions",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionDefinitions_Tenants_TenantId",
                table: "ActionDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ActionDefinitions_TenantId_Name",
                table: "ActionDefinitions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ActionDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_ActionDefinitions_Name",
                table: "ActionDefinitions",
                column: "Name",
                unique: true);
        }
    }
}
