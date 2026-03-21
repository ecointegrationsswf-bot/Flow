using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMustChangePasswordAnd2FA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "SuperAdmins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorCode",
                table: "SuperAdmins",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorExpiry",
                table: "SuperAdmins",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorCode",
                table: "AppUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorExpiry",
                table: "AppUsers",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "SuperAdmins");

            migrationBuilder.DropColumn(
                name: "TwoFactorCode",
                table: "SuperAdmins");

            migrationBuilder.DropColumn(
                name: "TwoFactorExpiry",
                table: "SuperAdmins");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorCode",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorExpiry",
                table: "AppUsers");
        }
    }
}
