namespace MyCloud.Contracts.Events.Storage;

/// <summary>Dung lượng đã dùng vượt một ngưỡng (80 / 90 / 100 %) — Notification báo cho người dùng.</summary>
public sealed record QuotaThresholdReached(Guid UserId, int Percent, long UsedBytes, long LimitBytes) : IntegrationEvent;

/// <summary>Tệp zip người dùng yêu cầu đã sẵn sàng (hết hạn lúc <c>ExpiresAt</c>) — Notification báo cho người dùng.</summary>
public sealed record ArchiveReady(
    Guid ArchiveId, Guid UserId, string Name, long SizeBytes, int FileCount, int SkippedCount, DateTimeOffset ExpiresAt)
    : IntegrationEvent;

/// <summary>Không tạo được tệp zip (vd vượt giới hạn, lỗi đọc object).</summary>
public sealed record ArchiveFailed(Guid ArchiveId, Guid UserId, string Name, string Reason) : IntegrationEvent;
