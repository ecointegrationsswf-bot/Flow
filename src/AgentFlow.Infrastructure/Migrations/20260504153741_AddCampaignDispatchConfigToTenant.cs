using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignDispatchConfigToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CampaignDispatchEnabled",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "CampaignMaxPerDay",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "CampaignMaxPerHour",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<int>(
                name: "CampaignMessagesPerMinute",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledFor",
                table: "CampaignContacts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignContacts_DispatchStatus_ScheduledFor",
                table: "CampaignContacts",
                columns: new[] { "DispatchStatus", "ScheduledFor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignContacts_DispatchStatus_ScheduledFor",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "CampaignDispatchEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CampaignMaxPerDay",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CampaignMaxPerHour",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CampaignMessagesPerMinute",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ScheduledFor",
                table: "CampaignContacts");
        }
    }
}
