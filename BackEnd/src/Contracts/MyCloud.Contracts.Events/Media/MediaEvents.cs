namespace MyCloud.Contracts.Events.Media;

/// <summary>
/// Media Worker đã lưu thumbnail (WebP) cho một version của file. Drive đánh dấu item có thumbnail nếu version đó vẫn là
/// version hiện tại (event đến muộn cho version cũ bị bỏ qua).
/// </summary>
public sealed record ThumbnailGenerated(Guid ItemId, Guid OwnerId, Guid VersionId, Guid BlobId, IReadOnlyList<int> Sizes)
    : IntegrationEvent;
