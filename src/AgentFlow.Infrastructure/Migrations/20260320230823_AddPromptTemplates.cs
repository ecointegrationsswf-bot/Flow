using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromptTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnalysisPrompts = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FieldMapping = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptTemplates_AgentCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AgentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplates_CategoryId",
                table: "PromptTemplates",
                column: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptTemplates");
        }
    }
}
