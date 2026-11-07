using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Uploads;

/// <summary>
/// Upload đang diễn ra: nhớ file sẽ được tạo ở đâu, tên gì. Id = id upload session của Storage.
/// Khi hoàn tất, giữ lại <see cref="CompletedItemId"/> để lệnh "hoàn tất" gửi lặp (mạng chập chờn) trả cùng kết quả.
/// </summary>
public sealed class PendingUpload : Entity<Guid>
{
    private PendingUpload()
    {
    }

    public Guid OwnerId { get; private set; }

    public Guid ParentId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string MimeType { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Upload phiên bản mới cho file này thay vì tạo file mới (docs/03 §3 <c>targetItemId</c>).</summary>
    public Guid? TargetItemId { get; private set; }

    public Guid? CompletedItemId { get; private set; }

    public static PendingUpload Start(
        Guid sessionId, Guid ownerId, DriveItem parent, ItemName name, long sizeBytes, string mimeType, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (!parent.IsFolder)
        {
            throw new DriveException.NotAFolder(parent.Id);
        }

        if (parent.IsTrashed)
        {
            throw new DriveException.ItemTrashed(parent.Id);
        }

        return new PendingUpload
        {
            Id = sessionId,
            OwnerId = ownerId,
            ParentId = parent.Id,
            Name = name.Value,
            SizeBytes = sizeBytes,
            MimeType = mimeType,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
    }

    /// <summary>Upload nội dung mới cho <paramref name="file"/>; khi hoàn tất thành version kế tiếp của nó.</summary>
    public static PendingUpload StartNewVersion(
        Guid sessionId, Guid ownerId, DriveItem file, long sizeBytes, string mimeType, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (file.IsFolder)
        {
            throw new DriveException.NotAFile(file.Id);
        }

        if (file.IsTrashed)
        {
            throw new DriveException.ItemTrashed(file.Id);
        }

        return new PendingUpload
        {
            Id = sessionId,
            OwnerId = ownerId,
            ParentId = file.ParentId!.Value,
            Name = file.Name,
            SizeBytes = sizeBytes,
            MimeType = mimeType,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            TargetItemId = file.Id,
        };
    }

    public void MarkCompleted(Guid itemId) => CompletedItemId = itemId;
}
