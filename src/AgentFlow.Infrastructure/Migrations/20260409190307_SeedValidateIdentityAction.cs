using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedValidateIdentityAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insertar VALIDATE_IDENTITY para cada tenant activo que no la tenga
            migrationBuilder.Sql(@"
                INSERT INTO ActionDefinitions (Id, TenantId, Name, Description, RequiresWebhook, SendsEmail, SendsSms, IsActive, CreatedAt)
                SELECT
                    NEWID(),
                    t.Id,
                    'VALIDATE_IDENTITY',
                    'Valida identidad del cliente via webhook antes de entregar información confidencial',
                    1,
                    0,
                    0,
                    1,
                    GETUTCDATE()
                FROM Tenants t
                WHERE t.IsActive = 1
                  AND NOT EXISTS (
                    SELECT 1 FROM ActionDefinitions ad
                    WHERE ad.TenantId = t.Id AND ad.Name = 'VALIDATE_IDENTITY'
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ActionDefinitions WHERE Name = 'VALIDATE_IDENTITY';");
        }
    }
}
