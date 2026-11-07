using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Items;

// Domain event chỉ mang id (và dữ liệu không còn trên aggregate sau thay đổi, như cha cũ). Trạng thái đầy đủ được đọc
// từ aggregate khi chuyển thành integration event — sau khi audit đã được gán — nên event luôn khớp dữ liệu đã lưu.

public abstract record ItemDomainEvent : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ItemCreatedDomainEvent(Guid ItemId) : ItemDomainEvent;

public sealed record ItemRenamedDomainEvent(Guid ItemId) : ItemDomainEvent;

public sealed record ItemMovedDomainEvent(Guid ItemId, Guid OldParentId) : ItemDomainEvent;

public sealed record FileVersionAddedDomainEvent(Guid ItemId, Guid VersionId) : ItemDomainEvent;
