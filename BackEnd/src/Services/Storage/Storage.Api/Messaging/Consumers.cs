using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Contracts.Events.Identity;
using Storage.Api.Data;

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
