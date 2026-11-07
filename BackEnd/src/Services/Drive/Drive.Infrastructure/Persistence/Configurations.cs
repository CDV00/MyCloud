using Drive.Domain.Comments;
using Drive.Domain.Items;
using Drive.Domain.Stars;
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
        builder.Ignore(x => x.IsTrashed);
        builder.Ignore(x => x.IsTrashRoot);
        builder.Ignore(x => x.HasThumbnail);

        // NO ACTION (kiểm tra cuối câu lệnh) thay vì RESTRICT: xoá vĩnh viễn cả cha lẫn con trong một câu DELETE.
        builder.HasOne<DriveItem>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Không trùng tên (không phân biệt hoa thường, đã chuẩn hoá NFC) trong cùng thư mục — item trong thùng rác không giữ tên.
        builder.HasIndex(x => new { x.ParentId, x.NameKey }).IsUnique().HasFilter("trashed_at IS NULL").HasDatabaseName(UniqueNameIndex);
        // Liệt kê thư mục: thư mục trước, theo tên.
        builder.HasIndex(x => new { x.ParentId, x.Kind, x.NameKey });
        // Cây con: WHERE path LIKE '/a/b/%'.
        builder.HasIndex(x => x.Path).HasOperators("text_pattern_ops");
        // Mỗi người dùng đúng một thư mục gốc.
        builder.HasIndex(x => x.OwnerId).IsUnique().HasFilter("parent_id IS NULL").HasDatabaseName(UniqueRootIndex);
        // Thùng rác của một người (gốc các nhóm) + job xoá sau 30 ngày.
        builder.HasIndex(x => new { x.OwnerId, x.TrashedAt }).HasFilter("trash_root_id = id").HasDatabaseName("ix_items_trash_roots");
        // Khôi phục cả nhóm: WHERE trash_root_id = :id.
        builder.HasIndex(x => x.TrashRootId).HasFilter("trash_root_id IS NOT NULL");
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
        builder.Property(x => x.Source).HasConversion<short>().HasDefaultValue(VersionSource.Upload).HasSentinel(VersionSource.Upload);
        builder.Property(x => x.Note).HasMaxLength(FileVersion.MaxNoteLength);
        builder.HasIndex(x => new { x.ItemId, x.VersionNo }).IsUnique();
        builder.HasIndex(x => x.BlobId);
    }
}

internal sealed class ItemCommentConfiguration : IEntityTypeConfiguration<ItemComment>
{
    public void Configure(EntityTypeBuilder<ItemComment> builder)
    {
        builder.ToTable("item_comments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Body).HasMaxLength(ItemComment.MaxLength).IsRequired();
        builder.Ignore(x => x.IsDeleted);
        // Xoá vĩnh viễn tệp ⇒ xoá luôn trao đổi. Phiên bản bị xoá thì giữ tin nhắn (hiện "phiên bản đã xoá").
        builder.HasOne<DriveItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ItemId, x.CreatedAt });
        builder.HasIndex(x => x.ParentId);
    }
}

internal sealed class StarConfiguration : IEntityTypeConfiguration<Star>
{
    public void Configure(EntityTypeBuilder<Star> builder)
    {
        builder.ToTable("stars");
        builder.HasKey(x => new { x.UserId, x.ItemId });
        builder.HasOne<DriveItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

/// <summary>Lần cuối đã publish <c>ItemAccessed</c> cho (người dùng, item) — để gộp theo 5 phút.</summary>
internal sealed class ItemAccessRecord
{
    public Guid UserId { get; set; }

    public Guid ItemId { get; set; }

    public DateTimeOffset LastPublishedAt { get; set; }
}

internal sealed class ItemAccessRecordConfiguration : IEntityTypeConfiguration<ItemAccessRecord>
{
    public void Configure(EntityTypeBuilder<ItemAccessRecord> builder)
    {
        builder.ToTable("item_access_log");
        builder.HasKey(x => new { x.UserId, x.ItemId });
        builder.HasOne<DriveItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
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
        builder.Property(x => x.Source).HasConversion<short>().HasDefaultValue(VersionSource.Upload).HasSentinel(VersionSource.Upload);
        builder.Property(x => x.Note).HasMaxLength(FileVersion.MaxNoteLength);
        builder.HasIndex(x => x.ExpiresAt);
    }
}
