using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLabelingJobHourUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: la columna pudo haber sido dropeada por el self-healing
            // de Program.cs en una iteración anterior.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'LabelingJobHourUtc')
    ALTER TABLE CampaignTemplates DROP COLUMN LabelingJobHourUtc;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CampaignTemplates') AND name = 'LabelingJobHourUtc')
    ALTER TABLE CampaignTemplates ADD LabelingJobHourUtc int NULL;");
        }
    }
}
