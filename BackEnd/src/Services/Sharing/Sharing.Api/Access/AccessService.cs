using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MyCloud.BuildingBlocks.Domain.Exceptions;
using MyCloud.Contracts.Events.Sharing;
using Sharing.Api.Data;

namespace Sharing.Api.Access;

/// <summary>Item cần kiểm tra cùng ngữ cảnh Drive cung cấp: chủ sở hữu và tổ tiên (từ gốc tới cha).</summary>
public sealed record ItemContext(Guid ItemId, Guid OwnerId, IReadOnlyList<Guid> AncestorIds)
{
    /// <summary>Chính item và mọi tổ tiên — quyền trên bất kỳ cái nào đều áp dụng cho item.</summary>
    public IEnumerable<Guid> Lineage => AncestorIds.Append(ItemId);
}

public sealed record Principal(Guid? UserId, string? LinkToken, string? LinkPassword);

public sealed record PermissionView(Guid Id, Guid ItemId, Guid UserId, Role Role, Guid GrantedBy, DateTimeOffset CreatedAt, bool Direct);

public sealed record LinkView(Guid Id, Guid ItemId, Role Role, bool HasPassword, DateTimeOffset? ExpiresAt, Guid CreatedBy, DateTimeOffset CreatedAt);

public sealed record LinkResolution(bool Found, bool RequiresPassword, Guid ItemId, Role Role);

public sealed class PermissionNotFoundException(Guid id) : NotFoundException($"Permission '{id}' was not found.")
{
    public override string Code => "SHARING.PERMISSION_NOT_FOUND";
}

public sealed class LinkNotFoundException(Guid id) : NotFoundException($"Link '{id}' was not found.")
{
    public override string Code => "SHARING.LINK_NOT_FOUND";
}

public sealed class InvalidRoleException() : BadRequestException("Role must be viewer, commenter or editor.")
{
    public override string Code => "SHARING.INVALID_ROLE";
}

/// <summary>
/// Quyết định quyền và quản lý chia sẻ (ADR-014). Vai trò hiệu lực = cao nhất trong: chủ sở hữu, quyền trực tiếp trên
/// item hoặc bất kỳ tổ tiên nào (kế thừa), link còn hiệu lực trỏ tới item hoặc tổ tiên. Thu hồi có hiệu lực ngay ở
/// lần kiểm tra kế tiếp vì không có cache quyền.
/// </summary>
public sealed class AccessService(
    SharingDbContext db,
    IPublishEndpoint publishEndpoint,
    IMemoryCache cache,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan PasswordCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly PasswordHasher<ShareLink> Hasher = new();

    public async Task<Role> CheckAsync(Principal principal, ItemContext item, CancellationToken ct) =>
        (await BatchCheckAsync(principal, [item], ct))[0];

    /// <summary>Một truy vấn cho mọi item (quyền trực tiếp của người dùng trên toàn bộ tổ tiên liên quan).</summary>
    public async Task<IReadOnlyList<Role>> BatchCheckAsync(Principal principal, IReadOnlyList<ItemContext> items, CancellationToken ct)
    {
        Guid[] lineage = items.SelectMany(i => i.Lineage).Distinct().ToArray();
        Dictionary<Guid, Role> granted = principal.UserId is { } userId && lineage.Length > 0
            ? await db.Permissions.AsNoTracking()
                .Where(p => p.UserId == userId && lineage.Contains(p.ItemId))
                .ToDictionaryAsync(p => p.ItemId, p => p.Role, ct)
            : [];
        ShareLink? link = await FindUsableLinkAsync(principal, ct);

        return items.Select(item =>
        {
            if (principal.UserId is { } user && user == item.OwnerId)
            {
                return Role.Owner;
            }

            Role role = item.Lineage.Select(id => granted.GetValueOrDefault(id)).DefaultIfEmpty(Role.None).Max();
            if (link is not null && item.Lineage.Contains(link.ItemId) && link.Role > role)
            {
                role = link.Role;
            }

            return role;
        }).ToArray();
    }

    /// <summary>Quyền trực tiếp trên item và quyền kế thừa từ tổ tiên (gần nhất trước).</summary>
    public async Task<IReadOnlyList<PermissionView>> ListAsync(ItemContext item, CancellationToken ct)
    {
        Guid[] lineage = item.Lineage.ToArray();
        List<Permission> found = await db.Permissions.AsNoTracking().Where(p => lineage.Contains(p.ItemId)).ToListAsync(ct);
        return found
            .OrderBy(p => p.ItemId == item.ItemId ? 0 : 1)
            .ThenByDescending(p => Array.IndexOf(lineage, p.ItemId))
            .ThenBy(p => p.CreatedAt)
            .Select(p => new PermissionView(p.Id, p.ItemId, p.UserId, p.Role, p.GrantedBy, p.CreatedAt, p.ItemId == item.ItemId))
            .ToArray();
    }

    /// <summary>Cấp quyền trực tiếp; đã có ⇒ đổi vai trò. Event đi qua outbox cùng transaction.</summary>
    public async Task<PermissionView> GrantAsync(
        ItemContext item, string itemName, string itemKind, Guid userId, Role role, Guid grantedBy, bool notify, string? message,
        CancellationToken ct, string? grantedByName = null, string? granteeEmail = null)
    {
        if (role is not (Role.Viewer or Role.Commenter or Role.Editor))
        {
            throw new InvalidRoleException();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Permission? existing = await db.Permissions.SingleOrDefaultAsync(p => p.ItemId == item.ItemId && p.UserId == userId, ct);
        if (existing is null)
        {
            existing = new Permission
            {
                Id = Guid.CreateVersion7(),
                ItemId = item.ItemId,
                OwnerId = item.OwnerId,
                UserId = userId,
                Role = role,
                GrantedBy = grantedBy,
                CreatedAt = now,
            };
            db.Permissions.Add(existing);
            await publishEndpoint.Publish(new ItemShared(
                existing.Id, item.ItemId, item.OwnerId, itemName, itemKind, userId, RoleName(role), grantedBy, notify,
                string.IsNullOrWhiteSpace(message) ? null : message.Trim(), grantedByName, granteeEmail), ct);
        }
        else if (existing.Role != role)
        {
            existing.Role = role;
            existing.UpdatedAt = now;
            await publishEndpoint.Publish(new ShareRoleChanged(existing.Id, item.ItemId, userId, RoleName(role), grantedBy), ct);
        }

        await db.SaveChangesAsync(ct);
        return new PermissionView(existing.Id, existing.ItemId, existing.UserId, existing.Role, existing.GrantedBy, existing.CreatedAt, true);
    }

    public async Task RevokeAsync(Guid itemId, Guid permissionId, Guid revokedBy, CancellationToken ct)
    {
        Permission permission = await db.Permissions.SingleOrDefaultAsync(p => p.Id == permissionId && p.ItemId == itemId, ct)
            ?? throw new PermissionNotFoundException(permissionId);
        db.Permissions.Remove(permission);
        await publishEndpoint.Publish(new ShareRevoked(permission.Id, permission.ItemId, permission.UserId, revokedBy), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Tạo link; trả token (32 byte ngẫu nhiên, base64url) — chỉ lưu SHA-256 của nó.</summary>
    public async Task<(LinkView Link, string Token)> CreateLinkAsync(
        ItemContext item, Role role, DateTimeOffset? expiresAt, string? password, Guid createdBy, CancellationToken ct)
    {
        if (role is not (Role.Viewer or Role.Commenter))
        {
            // Sửa qua link cần biết ai sửa (audit) ⇒ chỉ cấp sửa cho người dùng cụ thể (docs/07 Phase 4, lệch kế hoạch).
            throw new InvalidRoleException();
        }

        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var link = new ShareLink
        {
            Id = Guid.CreateVersion7(),
            ItemId = item.ItemId,
            OwnerId = item.OwnerId,
            TokenHash = HashToken(token),
            Role = role,
            ExpiresAt = expiresAt,
            CreatedBy = createdBy,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        if (!string.IsNullOrEmpty(password))
        {
            link.PasswordHash = Hasher.HashPassword(link, password);
        }

        db.Links.Add(link);
        await db.SaveChangesAsync(ct);
        return (ToView(link), token);
    }

    public async Task<IReadOnlyList<LinkView>> ListLinksAsync(Guid itemId, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ShareLink> links = await db.Links.AsNoTracking()
            .Where(l => l.ItemId == itemId && l.DisabledAt == null && (l.ExpiresAt == null || l.ExpiresAt > now))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);
        return links.Select(ToView).ToArray();
    }

    public async Task DisableLinkAsync(Guid itemId, Guid linkId, CancellationToken ct)
    {
        int updated = await db.Links
            .Where(l => l.Id == linkId && l.ItemId == itemId && l.DisabledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.DisabledAt, timeProvider.GetUtcNow()), ct);
        if (updated == 0 && !await db.Links.AnyAsync(l => l.Id == linkId && l.ItemId == itemId, ct))
        {
            throw new LinkNotFoundException(linkId);
        }
    }

    public async Task<LinkResolution> ResolveLinkAsync(string token, string? password, CancellationToken ct)
    {
        ShareLink? link = await FindActiveLinkAsync(token, ct);
        if (link is null)
        {
            return new LinkResolution(false, false, Guid.Empty, Role.None);
        }

        return PasswordAccepted(link, token, password)
            ? new LinkResolution(true, false, link.ItemId, link.Role)
            : new LinkResolution(true, true, Guid.Empty, Role.None);
    }

    /// <summary>Xoá quyền và link của các item đã bị xoá vĩnh viễn (event <c>ItemsPurged</c>).</summary>
    public async Task ForgetItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        await db.Permissions.Where(p => itemIds.Contains(p.ItemId)).ExecuteDeleteAsync(ct);
        await db.Links.Where(l => itemIds.Contains(l.ItemId)).ExecuteDeleteAsync(ct);
    }

    public static string RoleName(Role role) => role.ToString().ToLowerInvariant();

    private async Task<ShareLink?> FindUsableLinkAsync(Principal principal, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(principal.LinkToken))
        {
            return null;
        }

        ShareLink? link = await FindActiveLinkAsync(principal.LinkToken, ct);
        return link is not null && PasswordAccepted(link, principal.LinkToken, principal.LinkPassword) ? link : null;
    }

    private async Task<ShareLink?> FindActiveLinkAsync(string token, CancellationToken ct)
    {
        byte[] hash = HashToken(token);
        DateTimeOffset now = timeProvider.GetUtcNow();
        return await db.Links.AsNoTracking()
            .SingleOrDefaultAsync(l => l.TokenHash == hash && l.DisabledAt == null && (l.ExpiresAt == null || l.ExpiresAt > now), ct);
    }

    /// <summary>
    /// Mật khẩu link được kiểm tra ở mọi request truy cập; hash chậm (PBKDF2) nên kết quả đúng được nhớ 10 phút theo
    /// SHA-256(token + mật khẩu) — không lưu mật khẩu rõ trong bộ nhớ.
    /// </summary>
    private bool PasswordAccepted(ShareLink link, string token, string? password)
    {
        if (link.PasswordHash is null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        string key = "link-pw:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{link.Id:N}\n{token}\n{password}")));
        if (cache.TryGetValue(key, out _))
        {
            return true;
        }

        if (Hasher.VerifyHashedPassword(link, link.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return false;
        }

        cache.Set(key, true, PasswordCacheTtl);
        return true;
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static LinkView ToView(ShareLink l) =>
        new(l.Id, l.ItemId, l.Role, l.PasswordHash is not null, l.ExpiresAt, l.CreatedBy, l.CreatedAt);
}
