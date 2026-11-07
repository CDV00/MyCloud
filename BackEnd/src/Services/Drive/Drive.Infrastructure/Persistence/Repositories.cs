using Drive.Application.Uploads;
using Drive.Domain;
using Drive.Domain.Items;
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
        db.Items.AnyAsync(x => x.ParentId == parentId && x.NameKey == nameKey && x.Id != exceptItemId, ct);

    public async Task<IReadOnlySet<string>> NameKeysStartingWithAsync(Guid parentId, string keyPrefix, CancellationToken ct) =>
        (await db.Items.Where(x => x.ParentId == parentId && x.NameKey.StartsWith(keyPrefix))
            .Select(x => x.NameKey).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

    public async Task<IReadOnlyList<DriveItem>> ListChildrenAsync(
        Guid parentId, ChildOrder order, bool descending, int skip, int take, CancellationToken ct)
    {
        IOrderedQueryable<DriveItem> query = db.Items.AsNoTracking()
            .Where(x => x.ParentId == parentId)
            .OrderBy(x => x.Kind); // Folder (1) trước File (2), bất kể chiều sắp xếp

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

    public void Add(DriveItem item) => db.Items.Add(item);

    public void MoveSubtree(SubtreeMove move) =>
        // Một câu UPDATE cho cả cây con (ADR-005). Path chỉ gồm GUID dạng "N" và '/', không có ký tự đặc biệt của LIKE.
        db.ExecuteAfterSave(
            $"""
            UPDATE items
            SET path = {move.NewPath} || substr(path, {move.OldPath.Length + 1}),
                depth = depth + {move.DepthDelta}
            WHERE path LIKE {move.OldPath + "%"} AND id <> {move.ItemId}
            """);
}

internal sealed class PendingUploadRepository(DriveDbContext db) : IPendingUploadRepository
{
    public Task<PendingUpload?> FindAsync(Guid id, CancellationToken ct) =>
        db.PendingUploads.SingleOrDefaultAsync(x => x.Id == id, ct);

    public void Add(PendingUpload upload) => db.PendingUploads.Add(upload);
}

internal sealed class FileVersionReader(DriveDbContext db) : IFileVersionReader
{
    public async Task<FileVersion> GetAsync(Guid versionId, CancellationToken ct) =>
        await db.FileVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, ct)
        ?? throw new InvalidOperationException($"File version {versionId} is missing.");
}
