namespace MyCloud.Contracts.Events.Storage;

/// <summary>Dung lượng đã dùng vượt một ngưỡng (80 / 90 / 100 %) — Notification báo cho người dùng.</summary>
public sealed record QuotaThresholdReached(Guid UserId, int Percent, long UsedBytes, long LimitBytes) : IntegrationEvent;
