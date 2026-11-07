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

    public Guid? CompletedItemId { get; private set; }

    public static PendingUpload Start(
        Guid sessionId, Guid ownerId, DriveItem parent, ItemName name, long sizeBytes, string mimeType, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (!parent.IsFolder)
        {
            throw new DriveException.NotAFolder(parent.Id);
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

    public void MarkCompleted(Guid itemId) => CompletedItemId = itemId;
}
