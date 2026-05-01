using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMorosidadModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoPaisDefault",
                table: "Tenants",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "507");

            migrationBuilder.CreateTable(
                name: "ActionDelinquencyConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodigoPais = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "507"),
                    ItemsJsonPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AutoCrearCampanas = table.Column<bool>(type: "bit", nullable: false),
                    CampaignTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgentDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CampaignNamePattern = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NotificationEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionDelinquencyConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionDelinquencyConfigs_ActionDefinitions_ActionDefinitionId",
                        column: x => x.ActionDefinitionId,
                        principalTable: "ActionDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActionDelinquencyConfigs_AgentDefinitions_AgentDefinitionId",
                        column: x => x.AgentDefinitionId,
                        principalTable: "AgentDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActionDelinquencyConfigs_CampaignTemplates_CampaignTemplateId",
                        column: x => x.CampaignTemplateId,
                        principalTable: "CampaignTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActionDelinquencyConfigs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActionFieldMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogicalFieldKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JsonPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionFieldMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionFieldMappings_ActionDefinitions_ActionDefinitionId",
                        column: x => x.ActionDefinitionId,
                        principalTable: "ActionDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DelinquencyExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledWebhookJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    ProcessedItems = table.Column<int>(type: "int", nullable: false),
                    DiscardedItems = table.Column<int>(type: "int", nullable: false),
                    GroupsCreated = table.Column<int>(type: "int", nullable: false),
                    CampaignsCreated = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelinquencyExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelinquencyExecutions_ActionDefinitions_ActionDefinitionId",
                        column: x => x.ActionDefinitionId,
                        principalTable: "ActionDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelinquencyExecutions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LogicalFieldCatalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DataType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "string"),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogicalFieldCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNormalized = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactGroups_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ContactGroups_DelinquencyExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "DelinquencyExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContactGroups_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DelinquencyItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowIndex = table.Column<int>(type: "int", nullable: false),
                    PhoneRaw = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PhoneNormalized = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PolicyNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RawData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    DiscardReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelinquencyItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelinquencyItems_ContactGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ContactGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DelinquencyItems_DelinquencyExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "DelinquencyExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "LogicalFieldCatalog",
                columns: new[] { "Id", "DataType", "Description", "DisplayName", "IsActive", "IsRequired", "Key", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-0000-0000-000000000001"), "phone", null, "Número de Teléfono", true, true, "PhoneNumber", 1 },
                    { new Guid("11111111-0000-0000-0000-000000000002"), "string", null, "Nombre del Cliente", true, false, "ClientName", 2 },
                    { new Guid("11111111-0000-0000-0000-000000000003"), "string", null, "Número de Póliza", true, false, "PolicyNumber", 3 },
                    { new Guid("11111111-0000-0000-0000-000000000004"), "number", null, "Monto en Mora", true, false, "Amount", 4 },
                    { new Guid("11111111-0000-0000-0000-000000000005"), "string", null, "Aseguradora", true, false, "Insurer", 5 },
                    { new Guid("11111111-0000-0000-0000-000000000006"), "string", null, "Fecha de Vencimiento", true, false, "DueDate", 6 },
                    { new Guid("11111111-0000-0000-0000-000000000007"), "string", null, "Tipo de Póliza", true, false, "PolicyType", 7 },
                    { new Guid("11111111-0000-0000-0000-000000000008"), "string", null, "Email del Ejecutivo", true, false, "ExecutiveEmail", 8 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionDelinquencyConfigs_ActionDefinitionId",
                table: "ActionDelinquencyConfigs",
                column: "ActionDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionDelinquencyConfigs_AgentDefinitionId",
                table: "ActionDelinquencyConfigs",
                column: "AgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionDelinquencyConfigs_CampaignTemplateId",
                table: "ActionDelinquencyConfigs",
                column: "CampaignTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionDelinquencyConfigs_TenantId_ActionDefinitionId",
                table: "ActionDelinquencyConfigs",
                columns: new[] { "TenantId", "ActionDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionFieldMappings_ActionDefinitionId_LogicalFieldKey",
                table: "ActionFieldMappings",
                columns: new[] { "ActionDefinitionId", "LogicalFieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactGroups_CampaignId",
                table: "ContactGroups",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactGroups_ExecutionId_PhoneNormalized",
                table: "ContactGroups",
                columns: new[] { "ExecutionId", "PhoneNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactGroups_TenantId",
                table: "ContactGroups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyExecutions_ActionDefinitionId",
                table: "DelinquencyExecutions",
                column: "ActionDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyExecutions_TenantId_StartedAt",
                table: "DelinquencyExecutions",
                columns: new[] { "TenantId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyItems_ExecutionId",
                table: "DelinquencyItems",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyItems_GroupId",
                table: "DelinquencyItems",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DelinquencyItems_PhoneNormalized",
                table: "DelinquencyItems",
                column: "PhoneNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_LogicalFieldCatalog_Key",
                table: "LogicalFieldCatalog",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionDelinquencyConfigs");

            migrationBuilder.DropTable(
                name: "ActionFieldMappings");

            migrationBuilder.DropTable(
                name: "DelinquencyItems");

            migrationBuilder.DropTable(
                name: "LogicalFieldCatalog");

            migrationBuilder.DropTable(
                name: "ContactGroups");

            migrationBuilder.DropTable(
                name: "DelinquencyExecutions");

            migrationBuilder.DropColumn(
                name: "CodigoPaisDefault",
                table: "Tenants");
        }
    }
}
