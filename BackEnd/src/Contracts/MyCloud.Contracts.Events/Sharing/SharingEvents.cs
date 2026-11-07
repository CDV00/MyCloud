namespace MyCloud.Contracts.Events.Sharing;

// Event của Sharing service (docs/04 §3.2). Role: "viewer" | "commenter" | "editor".

/// <summary>Người dùng được cấp quyền trên một item. Notification gửi email + thông báo; Search ghi access_grants.</summary>
public sealed record ItemShared(
    Guid PermissionId,
    Guid ItemId,
    Guid OwnerId,
    string ItemName,
    string ItemKind,
    Guid GranteeId,
    string Role,
    Guid SharedBy,
    bool Notify,
    string? Message,
    string? SharedByName = null,
    string? GranteeEmail = null) : IntegrationEvent;

public sealed record ShareRoleChanged(Guid PermissionId, Guid ItemId, Guid GranteeId, string Role, Guid ChangedBy) : IntegrationEvent;

/// <summary>Thu hồi quyền trực tiếp (người dùng mất quyền ngay ở Drive; Search ẩn item khỏi "Được chia sẻ").</summary>
public sealed record ShareRevoked(Guid PermissionId, Guid ItemId, Guid GranteeId, Guid RevokedBy) : IntegrationEvent;
