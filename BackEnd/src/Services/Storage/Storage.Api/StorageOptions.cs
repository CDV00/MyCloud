using System.ComponentModel.DataAnnotations;

namespace Storage.Api;

/// <summary>Section <c>Storage</c>.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Bucket private chứa mọi blob. Tên này cũng là tiền tố đường dẫn mà Vite/nginx proxy sang MinIO.</summary>
    [Required]
    public string Bucket { get; set; } = "mycloud-blobs";

    /// <summary>
    /// Gốc URL công khai của object storage dùng để ký presigned URL (vd CDN, S3/R2 thật). Bỏ trống ⇒ ký theo origin
    /// của chính trình duyệt (request đi qua Vite/nginx cùng origin, không cần CORS) — xem ADR-013.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    [Range(5L * 1024 * 1024, 5L * 1024 * 1024 * 1024)]
    public long PartSizeBytes { get; set; } = 8L * 1024 * 1024;

    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    [Range(0, long.MaxValue)]
    public long DefaultQuotaBytes { get; set; } = 15L * 1024 * 1024 * 1024;

    /// <summary>Upload chưa hoàn tất sau khoảng này bị huỷ và trả quota.</summary>
    public TimeSpan UploadSessionTtl { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan PartUrlTtl { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan DownloadUrlTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Blob đã upload nhưng Drive không "claim" sau khoảng này bị xoá (ADR-006).</summary>
    public TimeSpan UnclaimedBlobTtl { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>URL thumbnail (trình duyệt cache trong thời gian này).</summary>
    public TimeSpan ThumbnailUrlTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Tệp zip được giữ trong khoảng này rồi bị xoá (không tính quota).</summary>
    public TimeSpan ArchiveTtl { get; set; } = TimeSpan.FromHours(24);

    [Range(1, long.MaxValue)]
    public long MaxArchiveBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    [Range(1, 100_000)]
    public int MaxArchiveEntries { get; set; } = 10_000;

    /// <summary>Số tệp zip được nén đồng thời trên mỗi instance.</summary>
    [Range(1, 16)]
    public int ArchiveConcurrency { get; set; } = 2;

    /// <summary>Số URL part ký sẵn khi tạo session; phần còn lại client xin thêm.</summary>
    [Range(1, 1000)]
    public int InitialSignedParts { get; set; } = 50;

    /// <summary>Kích thước part: tối thiểu <see cref="PartSizeBytes"/>, đủ lớn để không vượt 10.000 part của S3.</summary>
    public long PartSizeFor(long sizeBytes) => Math.Max(PartSizeBytes, (long)Math.Ceiling(sizeBytes / 10_000d));
}
