using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsProcessToActionDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: el self-healing de Program.cs puede haber agregado la columna.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActionDefinitions') AND name = 'IsProcess')
    ALTER TABLE ActionDefinitions ADD IsProcess bit NOT NULL DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ActionDefinitions') AND name = 'IsProcess')
    ALTER TABLE ActionDefinitions DROP COLUMN IsProcess;");
        }
    }
}
