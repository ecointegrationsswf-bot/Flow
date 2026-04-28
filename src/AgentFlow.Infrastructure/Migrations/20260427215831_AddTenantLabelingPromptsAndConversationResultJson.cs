using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLabelingPromptsAndConversationResultJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: las columnas pueden haber sido agregadas vía SQL manual durante el desarrollo.
            migrationBuilder.Sql(@"
IF COL_LENGTH('Tenants', 'LabelingAnalysisPrompt') IS NULL
    ALTER TABLE Tenants ADD LabelingAnalysisPrompt nvarchar(max) NULL;
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('Tenants', 'LabelingResultSchemaPrompt') IS NULL
    ALTER TABLE Tenants ADD LabelingResultSchemaPrompt nvarchar(max) NULL;
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('Conversations', 'LabelingResultJson') IS NULL
    ALTER TABLE Conversations ADD LabelingResultJson nvarchar(max) NULL;
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('Campaigns', 'LabelingSummarySentAt') IS NULL
    ALTER TABLE Campaigns ADD LabelingSummarySentAt datetime2 NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LabelingAnalysisPrompt", table: "Tenants");
            migrationBuilder.DropColumn(name: "LabelingResultSchemaPrompt", table: "Tenants");
            migrationBuilder.DropColumn(name: "LabelingResultJson", table: "Conversations");
            migrationBuilder.DropColumn(name: "LabelingSummarySentAt", table: "Campaigns");
        }
    }
}
