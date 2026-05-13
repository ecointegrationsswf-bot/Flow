using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <summary>
    /// Índice de soporte para el ConversationLabelingJob: permite filtrar por
    /// (TenantId, LabelId IS NULL OR LastActivityAt > LabeledAt) sin scan completo.
    /// Aditivo — no modifica datos ni columnas existentes.
    /// </summary>
    public partial class AddLabelingIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Conversations_Labeling' AND object_id = OBJECT_ID('Conversations'))
    CREATE INDEX IX_Conversations_Labeling
        ON Conversations (TenantId, LabelId, LastActivityAt)
        INCLUDE (LabeledAt, CampaignId);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Conversations_Labeling' AND object_id = OBJECT_ID('Conversations'))
    DROP INDEX IX_Conversations_Labeling ON Conversations;");
        }
    }
}
