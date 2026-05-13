using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsPrimaryForAgentToCampaignTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Crear la columna con default true para retrocompat — los maestros
            //    existentes asumen el rol primario inicialmente.
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimaryForAgent",
                table: "CampaignTemplates",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // 2) Backfill defensivo: si por casualidad ya hay >1 maestro ACTIVO
            //    para el mismo agente, dejar como primario únicamente al más
            //    reciente (UpdatedAt DESC). Los demás bajan a IsPrimaryForAgent=0.
            //    Sin esto el filtered unique index del paso 3 fallaría con
            //    duplicate key.
            migrationBuilder.Sql(@"
WITH Ranked AS (
    SELECT
        Id,
        ROW_NUMBER() OVER (
            PARTITION BY TenantId, AgentDefinitionId
            ORDER BY UpdatedAt DESC, CreatedAt DESC
        ) AS rn
    FROM CampaignTemplates
    WHERE IsActive = 1
)
UPDATE ct
SET IsPrimaryForAgent = 0
FROM CampaignTemplates ct
JOIN Ranked r ON r.Id = ct.Id
WHERE r.rn > 1;

-- Los maestros desactivados (IsActive=0) también se bajan a 0 para evitar
-- ruido en el flag. El filtered index ya los excluye, pero el dato queda limpio.
UPDATE CampaignTemplates SET IsPrimaryForAgent = 0 WHERE IsActive = 0;
");

            // 3) Filtered unique index: garantiza 1 primario por (Tenant, Agente)
            //    entre los maestros activos. Excluye los inactivos del unique.
            migrationBuilder.CreateIndex(
                name: "UX_CampaignTemplate_PrimaryPerAgent",
                table: "CampaignTemplates",
                columns: new[] { "TenantId", "AgentDefinitionId" },
                unique: true,
                filter: "[IsPrimaryForAgent] = 1 AND [IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CampaignTemplate_PrimaryPerAgent",
                table: "CampaignTemplates");

            migrationBuilder.DropColumn(
                name: "IsPrimaryForAgent",
                table: "CampaignTemplates");
        }
    }
}
