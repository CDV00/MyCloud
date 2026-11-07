using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Events;

// Domain event chỉ mang Id: trạng thái đầy đủ được đọc từ aggregate lúc chuyển thành integration event
// (sau khi audit đã được gán), nên event luôn phản ánh dữ liệu thực sự được lưu.

public abstract record DriveDomainEvent : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record FolderCreatedDomainEvent(Guid FolderId) : DriveDomainEvent;

public sealed record FolderUpdatedDomainEvent(Guid FolderId) : DriveDomainEvent;

public sealed record FolderDeletedDomainEvent(Guid FolderId) : DriveDomainEvent;

public sealed record FileCreatedDomainEvent(Guid FileId) : DriveDomainEvent;

public sealed record FileUpdatedDomainEvent(Guid FileId) : DriveDomainEvent;

public sealed record FileDeletedDomainEvent(Guid FileId, string StorageKey) : DriveDomainEvent;
