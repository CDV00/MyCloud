namespace Storage.Api.Data;

public enum BlobStatus
{
    /// <summary>Đang upload (multipart chưa hoàn tất), quota đang được giữ chỗ.</summary>
    Pending = 1,

    /// <summary>Đã upload xong, quota đã tính; chờ Drive "claim".</summary>
    Uploaded = 2,

    /// <summary>Drive đã ghi nhận (FileVersionAdded) — không bị GC.</summary>
    Claimed = 3,

    /// <summary>Upload bị huỷ / hết hạn / sai kích thước.</summary>
    Aborted = 4,

    /// <summary>Object đã xoá, quota đã trả.</summary>
    Deleted = 5,
}

/// <summary>
/// Một object trong bucket + trạng thái upload của nó. Gộp "upload session" vào blob (1 session ↔ 1 blob) thay vì
/// 2 bảng như bản nháp ở docs/03 §4 — ít join, cùng một vòng đời.
/// </summary>
public sealed class Blob
{
    public Guid Id { get; set; }

    /// <summary>Người trả dung lượng (chủ sở hữu file).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Người đang upload — khác <see cref="OwnerId"/> khi upload vào thư mục được chia sẻ; chỉ người này ký thêm part.</summary>
    public Guid UploaderId { get; set; }

    public string Bucket { get; set; } = string.Empty;

    /// <summary><c>u/{ownerId}/{blobId}</c> — không chứa tên file của người dùng.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    /// <summary>Kích thước client khai báo — đã giữ chỗ quota theo số này.</summary>
    public long ExpectedSizeBytes { get; set; }

    /// <summary>Kích thước thật sau khi upload (HEAD object).</summary>
    public long? SizeBytes { get; set; }

    public BlobStatus Status { get; set; }

    public string? S3UploadId { get; set; }

    public long PartSizeBytes { get; set; }

    public int PartCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Hạn hoàn tất upload.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UploadedAt { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }

    public static string KeyFor(Guid ownerId, Guid blobId) => $"u/{ownerId:N}/{blobId:N}";
}

/// <summary>Dung lượng của một user. Cập nhật bằng câu UPDATE có điều kiện (atomic), không đọc-rồi-ghi.</summary>
public sealed class Quota
{
    public Guid UserId { get; set; }

    public long LimitBytes { get; set; }

    public long UsedBytes { get; set; }

    public long ReservedBytes { get; set; }
}
