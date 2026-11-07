using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Archives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entries_json = table.Column<string>(type: "jsonb", nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    file_count = table.Column<int>(type: "integer", nullable: false),
                    total_bytes = table.Column<long>(type: "bigint", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    s3upload_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archives", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archives_status_created_at",
                table: "archives",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_archives_user_id",
                table: "archives",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archives");
        }
    }
}
