using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Contracts.Events.Identity;
using Storage.Api.Data;
using Storage.Api.ObjectStorage;

namespace Storage.Api.Messaging;

/// <summary>Tạo quota mặc định cho người dùng mới (Storage cũng tự tạo khi cần nếu event đến muộn).</summary>
public sealed class UserRegisteredConsumer(StorageDbContext db, QuotaStore quotas) : IConsumer<UserRegistered>
{
    public Task Consume(ConsumeContext<UserRegistered> context) =>
        quotas.EnsureAsync(db, context.Message.UserId, context.CancellationToken);
}

/// <summary>
/// Drive đã ghi nhận nội dung file ⇒ blob được "claim", không bị GC (ADR-006). Idempotent: chỉ đổi Uploaded → Claimed.
/// </summary>
public sealed class FileVersionAddedConsumer(StorageDbContext db, TimeProvider timeProvider) : IConsumer<FileVersionAdded>
{
    public Task Consume(ConsumeContext<FileVersionAdded> context)
    {
        FileVersionAdded e = context.Message;
        DateTimeOffset now = timeProvider.GetUtcNow();
        return db.Blobs
            .Where(b => b.Id == e.BlobId && b.OwnerId == e.OwnerId && b.Status == BlobStatus.Uploaded)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BlobStatus.Claimed).SetProperty(b => b.ClaimedAt, now), context.CancellationToken);
    }
}

/// <summary>Item bị xoá vĩnh viễn (thùng rác) ⇒ xoá object của mọi version, trả quota.</summary>
public sealed class ItemsPurgedConsumer(BlobReclaimer reclaimer) : IConsumer<ItemsPurged>
{
    public Task Consume(ConsumeContext<ItemsPurged> context) =>
        reclaimer.DeleteAsync(context.Message.OwnerId, context.Message.BlobIds, context.CancellationToken);
}

/// <summary>Version cũ bị xoá (giới hạn số version / người dùng xoá) ⇒ xoá object, trả quota.</summary>
public sealed class FileVersionsDeletedConsumer(BlobReclaimer reclaimer) : IConsumer<FileVersionsDeleted>
{
    public Task Consume(ConsumeContext<FileVersionsDeleted> context) =>
        reclaimer.DeleteAsync(context.Message.OwnerId, context.Message.BlobIds, context.CancellationToken);
}

/// <summary>
/// Xoá blob mà Drive không còn dùng. Idempotent và an toàn khi giao lại: xoá object TRƯỚC (S3 trả thành công cả khi
/// object đã mất), rồi mới đổi trạng thái → Deleted bằng UPDATE có điều kiện; chỉ lần đổi được trạng thái mới trả quota.
/// Consumer chạy trong transaction của EF outbox/inbox nên đổi trạng thái + trả quota là nguyên tử.
/// </summary>
public sealed class BlobReclaimer(StorageDbContext db, QuotaStore quotas, S3ObjectStorage s3)
{
    public async Task<int> DeleteAsync(Guid ownerId, IReadOnlyCollection<Guid> blobIds, CancellationToken ct)
    {
        if (blobIds.Count == 0)
        {
            return 0;
        }

        // Blob của người khác bị bỏ qua (event chỉ đến từ Drive, nhưng không tin mù quáng vào id).
        List<Blob> blobs = await db.Blobs.AsNoTracking()
            .Where(b => blobIds.Contains(b.Id) && b.OwnerId == ownerId
                && (b.Status == BlobStatus.Uploaded || b.Status == BlobStatus.Claimed))
            .ToListAsync(ct);
        if (blobs.Count == 0)
        {
            return 0;
        }

        await s3.DeleteManyAsync(blobs.Select(b => b.ObjectKey).ToArray(), ct);

        int deleted = 0;
        foreach (Blob blob in blobs)
        {
            bool transitioned = await db.Blobs
                .Where(b => b.Id == blob.Id && b.Status == blob.Status)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BlobStatus.Deleted), ct) == 1;
            if (transitioned)
            {
                await quotas.ReleaseUsedAsync(db, ownerId, blob.SizeBytes ?? blob.ExpectedSizeBytes, ct);
                deleted++;
            }
        }

        return deleted;
    }
}
