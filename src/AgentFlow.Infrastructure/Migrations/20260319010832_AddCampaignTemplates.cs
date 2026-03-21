using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignTemplateId",
                table: "Campaigns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AgentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FollowUpHours = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AutoCloseHours = table.Column<int>(type: "int", nullable: false),
                    LabelIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SendEmail = table.Column<bool>(type: "bit", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SendGridApiKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignTemplates_AgentDefinitions_AgentDefinitionId",
                        column: x => x.AgentDefinitionId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CampaignTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CampaignTemplateId",
                table: "Campaigns",
                column: "CampaignTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTemplates_AgentDefinitionId",
                table: "CampaignTemplates",
                column: "AgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTemplates_TenantId",
                table: "CampaignTemplates",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_CampaignTemplates_CampaignTemplateId",
                table: "Campaigns",
                column: "CampaignTemplateId",
                principalTable: "CampaignTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_CampaignTemplates_CampaignTemplateId",
                table: "Campaigns");

            migrationBuilder.DropTable(
                name: "CampaignTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_CampaignTemplateId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "CampaignTemplateId",
                table: "Campaigns");
        }
    }
}
