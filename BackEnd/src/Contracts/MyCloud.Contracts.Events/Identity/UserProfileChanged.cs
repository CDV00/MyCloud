namespace MyCloud.Contracts.Events.Identity;

/// <summary>Tên hiển thị / avatar thay đổi — service lưu bản sao (Search, Sharing) cập nhật theo.</summary>
public sealed record UserProfileChanged(Guid UserId, string DisplayName, string? AvatarUrl) : IntegrationEvent;
