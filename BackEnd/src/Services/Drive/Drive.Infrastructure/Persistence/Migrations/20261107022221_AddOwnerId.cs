using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "folders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "files",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Dữ liệu tạo trước Phase 1: người tạo là chủ sở hữu. Bản ghi tạo khi API chưa yêu cầu đăng nhập
            // có created_by = Guid.Empty và sẽ không ai truy cập được (chỉ có ở môi trường dev).
            migrationBuilder.Sql("UPDATE folders SET owner_id = created_by;");
            migrationBuilder.Sql("UPDATE files SET owner_id = created_by;");

            migrationBuilder.CreateIndex(
                name: "ix_folders_owner_id_parent_id",
                table: "folders",
                columns: new[] { "owner_id", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_files_owner_id_folder_id",
                table: "files",
                columns: new[] { "owner_id", "folder_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_folders_owner_id_parent_id",
                table: "folders");

            migrationBuilder.DropIndex(
                name: "ix_files_owner_id_folder_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "files");
        }
    }
}
