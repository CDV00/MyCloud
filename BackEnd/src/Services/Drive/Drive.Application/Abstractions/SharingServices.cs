using Drive.Domain.Items;

namespace Drive.Application.Abstractions;

public sealed record SharedPermission(Guid Id, Guid ItemId, Guid UserId, AccessRole Role, Guid GrantedBy, DateTimeOffset CreatedAt, bool Direct);

public sealed record SharedLink(Guid Id, Guid ItemId, AccessRole Role, bool HasPassword, DateTimeOffset? ExpiresAt, Guid CreatedBy, DateTimeOffset CreatedAt);

public sealed record CreatedLink(SharedLink Link, string Token);

public sealed record LinkTarget(bool Found, bool RequiresPassword, Guid ItemId, AccessRole Role);

/// <summary>Sharing service (gRPC, định danh service). Drive luôn gửi kèm chủ sở hữu + tổ tiên của item (ADR-014).</summary>
public interface ISharingClient
{
    Task<IReadOnlyList<AccessRole>> BatchCheckAsync(AccessPrincipal principal, IReadOnlyList<DriveItem> items, CancellationToken ct);

    Task<IReadOnlyList<SharedPermission>> ListPermissionsAsync(DriveItem item, CancellationToken ct);

    /// <param name="grantee">Người nhận (email cho thư thông báo).</param>
    /// <param name="sharerName">Tên người chia sẻ (hiển thị trong thông báo).</param>
    Task<SharedPermission> GrantAsync(
        DriveItem item, UserInfo grantee, AccessRole role, Guid grantedBy, string? sharerName, bool notify, string? message, CancellationToken ct);

    Task RevokeAsync(Guid itemId, Guid permissionId, Guid revokedBy, CancellationToken ct);

    Task<CreatedLink> CreateLinkAsync(DriveItem item, AccessRole role, DateTimeOffset? expiresAt, string? password, Guid createdBy, CancellationToken ct);

    Task<IReadOnlyList<SharedLink>> ListLinksAsync(Guid itemId, CancellationToken ct);

    Task DisableLinkAsync(Guid itemId, Guid linkId, CancellationToken ct);

    Task<LinkTarget> ResolveLinkAsync(string token, string? password, CancellationToken ct);
}

public sealed record UserInfo(Guid Id, string Email, string DisplayName, string? AvatarUrl);

/// <summary>Identity service (gRPC): tra người dùng để chia sẻ theo email và hiển thị tên.</summary>
public interface IUserDirectory
{
    Task<UserInfo?> FindByEmailAsync(string email, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, UserInfo>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
