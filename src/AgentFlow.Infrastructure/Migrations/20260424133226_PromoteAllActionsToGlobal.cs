using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PromoteAllActionsToGlobal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Capturar las acciones actualmente scopadas a cada tenant dentro de
            //    Tenant.AssignedActionIds (JSON nvarchar(max)) — merge con cualquier
            //    valor previo — para que al promocionar a globales cada tenant siga
            //    viendo sólo las que ya tenía.
            //    Formato JSON: ["guid1","guid2",...]  (lo que produce JsonSerializer para List<Guid>).
            migrationBuilder.Sql(@"
                UPDATE T
                SET AssignedActionIds = (
                    SELECT '[' + STRING_AGG('""' + LOWER(CAST(ids.Id AS NVARCHAR(36))) + '""', ',')
                           WITHIN GROUP (ORDER BY ids.Id) + ']'
                    FROM (
                        SELECT a.Id
                        FROM ActionDefinitions a
                        WHERE a.TenantId = T.Id

                        UNION

                        SELECT a2.Id
                        FROM ActionDefinitions a2
                        WHERE T.AssignedActionIds IS NOT NULL
                          AND T.AssignedActionIds <> ''
                          AND T.AssignedActionIds <> '[]'
                          AND EXISTS (
                              SELECT 1 FROM OPENJSON(T.AssignedActionIds)
                              WHERE LOWER(CAST([value] AS NVARCHAR(36))) = LOWER(CAST(a2.Id AS NVARCHAR(36)))
                          )
                    ) ids
                )
                FROM Tenants T
                WHERE EXISTS (SELECT 1 FROM ActionDefinitions WHERE TenantId = T.Id)
                   OR (T.AssignedActionIds IS NOT NULL AND T.AssignedActionIds <> '' AND T.AssignedActionIds <> '[]');
            ");

            // 2) Renombrar duplicados de Name antes de promocionar (el índice global
            //    de nombre es único). Se preserva la acción más antigua con el nombre
            //    original y se sufija el resto.
            migrationBuilder.Sql(@"
                WITH numbered AS (
                    SELECT Id, Name,
                           ROW_NUMBER() OVER (PARTITION BY Name ORDER BY CreatedAt, Id) AS rn
                    FROM ActionDefinitions
                )
                UPDATE ad
                SET Name = ad.Name + '_' + CAST(n.rn AS NVARCHAR(3)),
                    UpdatedAt = SYSUTCDATETIME()
                FROM ActionDefinitions ad
                INNER JOIN numbered n ON n.Id = ad.Id
                WHERE n.rn > 1;
            ");

            // 3) Promover todo a global.
            migrationBuilder.Sql(@"
                UPDATE ActionDefinitions
                SET TenantId = NULL
                WHERE TenantId IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No reversible: la asignación tenant→acción se perdió al promocionar.
            // Para revertir haría falta un snapshot previo. Se deja vacío a propósito.
        }
    }
}
