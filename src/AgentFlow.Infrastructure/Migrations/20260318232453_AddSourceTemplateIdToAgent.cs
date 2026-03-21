using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTemplateIdToAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceTemplateId",
                table: "AgentDefinitions",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceTemplateId",
                table: "AgentDefinitions");
        }
    }
}
