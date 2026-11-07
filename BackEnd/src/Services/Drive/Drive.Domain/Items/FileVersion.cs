using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Items;

/// <summary>Phiên bản được tạo bằng cách nào — hiển thị trong lịch sử chỉnh sửa.</summary>
public enum VersionSource
{
    /// <summary>Tải tệp lên (tệp mới hoặc "Tải lên phiên bản mới").</summary>
    Upload = 1,

    /// <summary>Lưu từ trình sửa trong trình duyệt.</summary>
    Edit = 2,

    /// <summary>Khôi phục một phiên bản cũ (<see cref="FileVersion.RestoredFromVersionNo"/>).</summary>
    Restore = 3,

    /// <summary>Bản sao của tệp khác ("Tạo bản sao").</summary>
    Copy = 4,
}

/// <summary>Một phiên bản nội dung của file; byte nằm trong blob của Storage service.</summary>
public sealed class FileVersion : Entity<Guid>, IAuditableEntity
{
    private FileVersion()
    {
    }

    public Guid ItemId { get; private set; }

    public int VersionNo { get; private set; }

    public Guid BlobId { get; private set; }

    public long SizeBytes { get; private set; }

    public string MimeType { get; private set; } = string.Empty;

    public VersionSource Source { get; private set; } = VersionSource.Upload;

    /// <summary>Số của phiên bản được khôi phục (khi <see cref="Source"/> = Restore).</summary>
    public int? RestoredFromVersionNo { get; private set; }

    /// <summary>Ghi chú thay đổi của người tạo phiên bản (lý do sửa / khôi phục), tối đa <see cref="MaxNoteLength"/> ký tự.</summary>
    public string? Note { get; private set; }

    public const int MaxNoteLength = 1000;

    /// <summary>Người tạo phiên bản hoặc chủ sở hữu tệp sửa / xoá ghi chú.</summary>
    public void SetNote(string? note) => Note = NormalizeNote(note);

    public static string? NormalizeNote(string? note)
    {
        string? text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return text is { Length: > MaxNoteLength } ? text[..MaxNoteLength] : text;
    }

    public DateTimeOffset CreatedOnUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? ModifiedOnUtc { get; set; }

    public Guid? ModifiedBy { get; set; }

    internal static FileVersion Create(
        Guid id, Guid itemId, int versionNo, Guid blobId, long sizeBytes, string mimeType, VersionSource source, int? restoredFrom,
        string? note) =>
        new()
        {
            Id = id,
            ItemId = itemId,
            VersionNo = versionNo,
            BlobId = blobId,
            SizeBytes = sizeBytes,
            MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType.Trim(),
            Source = source,
            RestoredFromVersionNo = source == VersionSource.Restore ? restoredFrom : null,
            Note = NormalizeNote(note),
        };
}
