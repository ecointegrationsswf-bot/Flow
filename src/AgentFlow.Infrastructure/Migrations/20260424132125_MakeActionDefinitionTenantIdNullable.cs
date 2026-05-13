using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeActionDefinitionTenantIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionDefinitions_Tenants_TenantId",
                table: "ActionDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ActionDefinitions_TenantId_Name",
                table: "ActionDefinitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ActionDefinitions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_ActionDefinitions_TenantId_Name",
                table: "ActionDefinitions",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            // Unicidad de nombre entre acciones globales (TenantId NULL).
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IX_ActionDefinitions_GlobalName
                ON ActionDefinitions(Name)
                WHERE TenantId IS NULL;
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_ActionDefinitions_Tenants_TenantId",
                table: "ActionDefinitions",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionDefinitions_Tenants_TenantId",
                table: "ActionDefinitions");

            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_ActionDefinitions_GlobalName ON ActionDefinitions;");

            migrationBuilder.DropIndex(
                name: "IX_ActionDefinitions_TenantId_Name",
                table: "ActionDefinitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ActionDefinitions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

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
    }
}
