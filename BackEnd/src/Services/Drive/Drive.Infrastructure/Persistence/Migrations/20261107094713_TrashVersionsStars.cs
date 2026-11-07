using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrashVersionsStars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_items_parent_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ux_items_parent_id_name_key",
                table: "items");

            migrationBuilder.AddColumn<Guid>(
                name: "target_item_id",
                table: "pending_uploads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_version_no",
                table: "items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "trash_root_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trashed_at",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "trashed_by",
                table: "items",
                type: "uuid",
                nullable: true);

            // File có từ Phase 2: số version mới nhất lấy từ lịch sử đã lưu.
            migrationBuilder.Sql(
                "UPDATE items SET last_version_no = v.max_no FROM " +
                "(SELECT item_id, max(version_no) AS max_no FROM file_versions GROUP BY item_id) v WHERE v.item_id = items.id");

            migrationBuilder.CreateTable(
                name: "item_access_log",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_access_log", x => new { x.user_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_item_access_log_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stars",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stars", x => new { x.user_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_stars_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_items_trash_root_id",
                table: "items",
                column: "trash_root_id",
                filter: "trash_root_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_trash_roots",
                table: "items",
                columns: new[] { "owner_id", "trashed_at" },
                filter: "trash_root_id = id");

            migrationBuilder.CreateIndex(
                name: "ux_items_parent_id_name_key",
                table: "items",
                columns: new[] { "parent_id", "name_key" },
                unique: true,
                filter: "trashed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_item_access_log_item_id",
                table: "item_access_log",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stars_item_id",
                table: "stars",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stars_user_id_created_at",
                table: "stars",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_items_items_parent_id",
                table: "items",
                column: "parent_id",
                principalTable: "items",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_items_parent_id",
                table: "items");

            migrationBuilder.DropTable(
                name: "item_access_log");

            migrationBuilder.DropTable(
                name: "stars");

            migrationBuilder.DropIndex(
                name: "ix_items_trash_root_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_trash_roots",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ux_items_parent_id_name_key",
                table: "items");

            migrationBuilder.DropColumn(
                name: "target_item_id",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "last_version_no",
                table: "items");

            migrationBuilder.DropColumn(
                name: "trash_root_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "trashed_at",
                table: "items");

            migrationBuilder.DropColumn(
                name: "trashed_by",
                table: "items");

            migrationBuilder.CreateIndex(
                name: "ux_items_parent_id_name_key",
                table: "items",
                columns: new[] { "parent_id", "name_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_items_items_parent_id",
                table: "items",
                column: "parent_id",
                principalTable: "items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
