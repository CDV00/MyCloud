using Drive.Domain;
using Drive.Domain.Items;
using Drive.Domain.Stars;
using Drive.Domain.Uploads;
using Microsoft.EntityFrameworkCore;

namespace Drive.Infrastructure.Persistence;

internal sealed class DriveItemRepository(DriveDbContext db) : IDriveItemRepository
{
    public Task<DriveItem?> FindAsync(Guid id, CancellationToken ct) =>
        db.Items.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<DriveItem?> FindRootAsync(Guid ownerId, CancellationToken ct) =>
        db.Items.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.ParentId == null, ct);

    public async Task<IReadOnlyList<DriveItem>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : await db.Items.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);

    public Task<bool> NameExistsAsync(Guid parentId, string nameKey, Guid? exceptItemId, CancellationToken ct) =>
        db.Items.AnyAsync(x => x.ParentId == parentId && x.NameKey == nameKey && x.Id != exceptItemId && x.TrashedAt == null, ct);

    public async Task<IReadOnlySet<string>> NameKeysStartingWithAsync(Guid parentId, string keyPrefix, CancellationToken ct) =>
        (await db.Items.Where(x => x.ParentId == parentId && x.TrashedAt == null && x.NameKey.StartsWith(keyPrefix))
            .Select(x => x.NameKey).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

    public async Task<IReadOnlyList<DriveItem>> ListChildrenAsync(
        Guid parentId, Guid? trashGroup, ChildOrder order, bool descending, int skip, int take, CancellationToken ct)
    {
        IQueryable<DriveItem> children = db.Items.AsNoTracking().Where(x => x.ParentId == parentId);
        children = trashGroup is { } group
            ? children.Where(x => x.TrashRootId == group)
            : children.Where(x => x.TrashedAt == null);

        IOrderedQueryable<DriveItem> query = children.OrderBy(x => x.Kind); // Folder (1) trước File (2), bất kể chiều sắp xếp
        query = (order, descending) switch
        {
            (ChildOrder.ModifiedAt, false) => query.ThenBy(x => x.ModifiedOnUtc ?? x.CreatedOnUtc),
            (ChildOrder.ModifiedAt, true) => query.ThenByDescending(x => x.ModifiedOnUtc ?? x.CreatedOnUtc),
            (ChildOrder.Size, false) => query.ThenBy(x => x.SizeBytes),
            (ChildOrder.Size, true) => query.ThenByDescending(x => x.SizeBytes),
            (_, false) => query.ThenBy(x => x.NameKey),
            (_, true) => query.ThenByDescending(x => x.NameKey),
        };

        return await query.ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DriveItem>> ListTrashAsync(Guid ownerId, int skip, int take, CancellationToken ct) =>
        await db.Items.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.TrashRootId == x.Id)
            .OrderByDescending(x => x.TrashedAt).ThenBy(x => x.Id)
            .Skip(skip).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<DriveItem>> ListSubtreeAsync(DriveItem folder, int take, CancellationToken ct) =>
        await db.Items.AsNoTracking()
            .Where(x => EF.Functions.Like(x.Path, folder.Path + "%") && x.Id != folder.Id && x.TrashedAt == null) // dùng index text_pattern_ops
            .OrderBy(x => x.Depth).ThenBy(x => x.Id)
            .Take(take).ToListAsync(ct);

    public void Add(DriveItem item) => db.Items.Add(item);

    // Các câu lệnh hàng loạt dưới đây chạy ngay sau SaveChanges, cùng transaction (ADR-005). Path chỉ gồm GUID dạng "N"
    // và '/', không có ký tự đặc biệt của LIKE.
    public void MoveSubtree(SubtreeMove move) =>
        db.ExecuteAfterSave(
            $"""
            UPDATE items
            SET path = {move.NewPath} || substr(path, {move.OldPath.Length + 1}),
                depth = depth + {move.DepthDelta}
            WHERE path LIKE {move.OldPath + "%"} AND id <> {move.ItemId}
            """);

    public void TrashSubtree(SubtreeTrash trash) =>
        // Item đã bị xoá riêng từ trước giữ nhóm của nó (khôi phục thư mục không kéo theo chúng).
        db.ExecuteAfterSave(
            $"""
            UPDATE items
            SET trashed_at = {trash.TrashedAt}, trash_root_id = {trash.ItemId}, trashed_by = {trash.By}, revision = revision + 1
            WHERE path LIKE {trash.Path + "%"} AND id <> {trash.ItemId} AND trashed_at IS NULL
            """);

    public void RestoreSubtree(SubtreeRestore restore)
    {
        db.ExecuteAfterSave(
            $"""
            UPDATE items
            SET trashed_at = NULL, trash_root_id = NULL, trashed_by = NULL, revision = revision + 1
            WHERE trash_root_id = {restore.TrashRootId} AND id <> {restore.TrashRootId}
            """);
        if (restore.Move is { } move)
        {
            MoveSubtree(move);
        }
    }
}

internal sealed class FileVersionRepository(DriveDbContext db) : IFileVersionRepository
{
    public Task<FileVersion?> FindAsync(Guid versionId, CancellationToken ct) =>
        db.FileVersions.SingleOrDefaultAsync(v => v.Id == versionId, ct);

    public async Task<IReadOnlyList<FileVersion>> FindManyAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken ct) =>
        versionIds.Count == 0 ? [] : await db.FileVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToListAsync(ct);

    public async Task<IReadOnlyList<FileVersion>> ListAsync(Guid itemId, CancellationToken ct) =>
        await db.FileVersions.Where(v => v.ItemId == itemId).OrderByDescending(v => v.VersionNo).ToListAsync(ct);

    public void Remove(FileVersion version) => db.FileVersions.Remove(version);
}

internal sealed class StarRepository(DriveDbContext db) : IStarRepository
{
    public Task<bool> IsStarredAsync(Guid userId, Guid itemId, CancellationToken ct) =>
        db.Stars.AnyAsync(s => s.UserId == userId && s.ItemId == itemId, ct);

    public async Task<IReadOnlySet<Guid>> StarredAmongAsync(Guid userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) =>
        (await db.Stars.Where(s => s.UserId == userId && itemIds.Contains(s.ItemId)).Select(s => s.ItemId).ToListAsync(ct)).ToHashSet();

    public Task AddAsync(Star star, CancellationToken ct) =>
        // Idempotent và an toàn khi gửi đồng thời: không đọc-rồi-ghi.
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO stars (user_id, item_id, created_at) VALUES ({star.UserId}, {star.ItemId}, {star.CreatedAt})
            ON CONFLICT (user_id, item_id) DO NOTHING
            """,
            ct);

    public Task RemoveAsync(Guid userId, Guid itemId, CancellationToken ct) =>
        db.Stars.Where(s => s.UserId == userId && s.ItemId == itemId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<DriveItem>> ListStarredItemsAsync(Guid userId, int skip, int take, CancellationToken ct) =>
        await db.Stars.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Join(db.Items.AsNoTracking().Where(i => i.TrashedAt == null), s => s.ItemId, i => i.Id, (s, i) => new { s.CreatedAt, Item = i })
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Item.Id)
            .Skip(skip).Take(take)
            .Select(x => x.Item)
            .ToListAsync(ct);
}

internal sealed class PendingUploadRepository(DriveDbContext db) : IPendingUploadRepository
{
    public Task<PendingUpload?> FindAsync(Guid id, CancellationToken ct) =>
        db.PendingUploads.SingleOrDefaultAsync(x => x.Id == id, ct);

    public void Add(PendingUpload upload) => db.PendingUploads.Add(upload);
}
