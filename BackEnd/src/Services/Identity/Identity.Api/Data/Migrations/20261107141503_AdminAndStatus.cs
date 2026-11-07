using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disabled_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_login_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "disabled_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_admin",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_login_at",
                table: "users");
        }
    }
}
