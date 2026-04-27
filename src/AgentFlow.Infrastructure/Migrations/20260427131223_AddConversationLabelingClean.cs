using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationLabelingClean : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fase 3: campos para etiquetado IA + LabelingJobHourUtc.
            // SQL crudo idempotente porque la BD puede tener residuos de iteraciones previas.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'LabelId')
    ALTER TABLE Conversations ADD LabelId uniqueidentifier NULL;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'LabeledAt')
    ALTER TABLE Conversations ADD LabeledAt datetime2 NULL;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'LabelingJobHourUtc')
    ALTER TABLE CampaignTemplates ADD LabelingJobHourUtc int NULL;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Conversations_LabelId' AND object_id = OBJECT_ID('Conversations'))
    CREATE INDEX IX_Conversations_LabelId ON Conversations (LabelId);");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Conversations_TenantId_Status_LabelId' AND object_id = OBJECT_ID('Conversations'))
    CREATE INDEX IX_Conversations_TenantId_Status_LabelId ON Conversations (TenantId, Status, LabelId);");

            // FK con ON DELETE NO ACTION (ConversationLabel ya cascadea desde Tenant
            // a Conversations, así que SetNull crearía multiple cascade paths).
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Conversations_ConversationLabels_LabelId')
    ALTER TABLE Conversations ADD CONSTRAINT FK_Conversations_ConversationLabels_LabelId
        FOREIGN KEY (LabelId) REFERENCES ConversationLabels(Id) ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Conversations_ConversationLabels_LabelId')
    ALTER TABLE Conversations DROP CONSTRAINT FK_Conversations_ConversationLabels_LabelId;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Conversations_LabelId' AND object_id = OBJECT_ID('Conversations'))
    DROP INDEX IX_Conversations_LabelId ON Conversations;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Conversations_TenantId_Status_LabelId' AND object_id = OBJECT_ID('Conversations'))
    DROP INDEX IX_Conversations_TenantId_Status_LabelId ON Conversations;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'LabelId')
    ALTER TABLE Conversations DROP COLUMN LabelId;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'LabeledAt')
    ALTER TABLE Conversations DROP COLUMN LabeledAt;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'LabelingJobHourUtc')
    ALTER TABLE CampaignTemplates DROP COLUMN LabelingJobHourUtc;");
        }
    }
}
