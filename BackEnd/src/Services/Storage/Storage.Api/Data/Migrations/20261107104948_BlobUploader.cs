using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storage.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BlobUploader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "uploader_id",
                table: "blobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Blob có từ trước: người upload chính là chủ sở hữu.
            migrationBuilder.Sql("UPDATE blobs SET uploader_id = owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "uploader_id",
                table: "blobs");
        }
    }
}
