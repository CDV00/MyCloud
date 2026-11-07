using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "source",
                table: "pending_uploads",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<int>(
                name: "restored_from_version_no",
                table: "file_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "source",
                table: "file_versions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "restored_from_version_no",
                table: "file_versions");

            migrationBuilder.DropColumn(
                name: "source",
                table: "file_versions");
        }
    }
}
