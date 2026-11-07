using Drive.Domain;
using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Drive.Application.Abstractions;

public enum ItemPermission
{
    View,
    Edit,
}

/// <summary>
/// Quyết định ai được làm gì với một item. Phase 2: chỉ chủ sở hữu. Phase 4 cắm Sharing service (OpenFGA) vào sau
/// interface này, giữ fast-path chủ sở hữu (docs/03 §3, ADR-007).
/// </summary>
public interface IItemAuthorizer
{
    Task<bool> IsAllowedAsync(Guid userId, DriveItem item, ItemPermission permission, CancellationToken ct);
}

public sealed class OwnerOnlyItemAuthorizer : IItemAuthorizer
{
    public Task<bool> IsAllowedAsync(Guid userId, DriveItem item, ItemPermission permission, CancellationToken ct) =>
        Task.FromResult(item.OwnerId == userId);
}

/// <summary>Nạp item + kiểm tra quyền. Không có quyền ⇒ 404 như không tồn tại (không lộ id hợp lệ).</summary>
public sealed class ItemAccess(IDriveItemRepository items, IItemAuthorizer authorizer, ICurrentUser currentUser)
{
    public Guid UserId => currentUser.GetRequiredUserId();

    public async Task<DriveItem> GetAsync(Guid itemId, ItemPermission permission, CancellationToken ct)
    {
        DriveItem? item = await items.FindAsync(itemId, ct);
        if (item is null || !await authorizer.IsAllowedAsync(UserId, item, permission, ct))
        {
            throw new DriveException.ItemNotFound(itemId);
        }

        return item;
    }

    public async Task<DriveItem> GetFolderAsync(Guid itemId, ItemPermission permission, CancellationToken ct)
    {
        DriveItem item = await GetAsync(itemId, permission, ct);
        return item.IsFolder ? item : throw new DriveException.NotAFolder(itemId);
    }
}
