using System.Text.Json;

namespace Storage.Api.Archives;

public enum ArchiveStatus
{
    /// <summary>Chờ worker nhận.</summary>
    Pending = 1,

    /// <summary>Đang nén (một worker đã nhận).</summary>
    Building = 2,

    /// <summary>Tải được tới <see cref="Archive.ExpiresAt"/>.</summary>
    Ready = 3,

    Failed = 4,

    /// <summary>Đã quá hạn, object đã xoá.</summary>
    Expired = 5,
}

/// <summary>Một mục trong zip. <see cref="BlobId"/> null ⇒ thư mục (đường dẫn kết thúc bằng "/").</summary>
public sealed record ArchiveItem(string Path, Guid? BlobId, long SizeBytes, string? MimeType)
{
    public bool IsFolder => BlobId is null;
}

/// <summary>
/// Tệp zip người dùng yêu cầu (tải nhiều item / cả thư mục — ADR-015). Danh sách entry do Drive dựng sau khi kiểm tra
/// quyền. Không tính quota; object <c>a/{userId}/{id}.zip</c> bị xoá sau <c>Storage:ArchiveTtl</c>.
/// </summary>
public sealed class Archive
{
    public Guid Id { get; set; }

    /// <summary>Người yêu cầu — người duy nhất tải được.</summary>
    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public ArchiveStatus Status { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>JSON của danh sách <see cref="ArchiveItem"/> (chỉ worker đọc, không truy vấn theo nội dung).</summary>
    public string EntriesJson { get; set; } = "[]";

    public int EntryCount { get; set; }

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }

    /// <summary>Kích thước tệp zip.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>File không còn khi nén (bị xoá vĩnh viễn trong lúc chờ) ⇒ bỏ qua.</summary>
    public int SkippedCount { get; set; }

    public int Attempts { get; set; }

    public string? S3UploadId { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public static string KeyFor(Guid userId, Guid archiveId) => $"a/{userId:N}/{archiveId:N}.zip";

    public IReadOnlyList<ArchiveItem> Items() =>
        JsonSerializer.Deserialize<List<ArchiveItem>>(EntriesJson, JsonOptions) ?? [];

    public static string Serialize(IReadOnlyList<ArchiveItem> items) => JsonSerializer.Serialize(items, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
