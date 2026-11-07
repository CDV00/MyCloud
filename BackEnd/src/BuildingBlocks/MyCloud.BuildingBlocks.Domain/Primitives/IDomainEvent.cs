namespace MyCloud.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Sự kiện nội bộ của một service. Được chuyển thành integration event
/// (MyCloud.Contracts.Events) ở tầng Infrastructure trước khi rời khỏi service.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
