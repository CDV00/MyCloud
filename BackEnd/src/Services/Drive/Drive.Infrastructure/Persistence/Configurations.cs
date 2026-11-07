using Drive.Domain.Items;
using Drive.Domain.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drive.Infrastructure.Persistence;

internal sealed class DriveItemConfiguration : IEntityTypeConfiguration<DriveItem>
{
    public const string UniqueNameIndex = "ux_items_parent_id_name_key";
    public const string UniqueRootIndex = "ux_items_owner_root";

    public void Configure(EntityTypeBuilder<DriveItem> builder)
    {
        builder.ToTable("items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.Name).HasMaxLength(ItemName.MaxLength).IsRequired();
        builder.Property(x => x.NameKey).HasMaxLength(ItemName.MaxLength).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(255);
        builder.Property(x => x.Path).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken(); // lưu đè thay đổi của người khác ⇒ lỗi 412
        builder.Ignore(x => x.AncestorIds);
        builder.Ignore(x => x.IsFolder);

        builder.HasOne<DriveItem>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Không trùng tên (không phân biệt hoa thường, đã chuẩn hoá NFC) trong cùng thư mục.
        builder.HasIndex(x => new { x.ParentId, x.NameKey }).IsUnique().HasDatabaseName(UniqueNameIndex);
        // Liệt kê thư mục: thư mục trước, theo tên.
        builder.HasIndex(x => new { x.ParentId, x.Kind, x.NameKey });
        // Cây con: WHERE path LIKE '/a/b/%'.
        builder.HasIndex(x => x.Path).HasOperators("text_pattern_ops");
        // Mỗi người dùng đúng một thư mục gốc.
        builder.HasIndex(x => x.OwnerId).IsUnique().HasFilter("parent_id IS NULL").HasDatabaseName(UniqueRootIndex);
    }
}

internal sealed class FileVersionConfiguration : IEntityTypeConfiguration<FileVersion>
{
    public void Configure(EntityTypeBuilder<FileVersion> builder)
    {
        builder.ToTable("file_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MimeType).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => new { x.ItemId, x.VersionNo }).IsUnique();
        builder.HasIndex(x => x.BlobId);
    }
}

internal sealed class PendingUploadConfiguration : IEntityTypeConfiguration<PendingUpload>
{
    public void Configure(EntityTypeBuilder<PendingUpload> builder)
    {
        builder.ToTable("pending_uploads");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(ItemName.MaxLength).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ExpiresAt);
    }
}
