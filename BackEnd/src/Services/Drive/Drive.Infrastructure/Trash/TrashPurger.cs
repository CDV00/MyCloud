using Drive.Application.Abstractions;
using Drive.Domain.Items;
using Drive.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyCloud.Contracts.Events.Drive;

namespace Drive.Infrastructure.Trash;

/// <summary>
/// Xoá vĩnh viễn cả cây con theo lô (docs/05 §6): mỗi lô là một transaction gồm DELETE + outbox <c>ItemsPurged</c>
/// (kèm blob của MỌI version để Storage xoá object, trả quota). Item sâu nhất xoá trước nên lô nào cũng không để lại
/// con mồ côi; dừng giữa chừng thì lần sau chạy tiếp phần còn lại.
/// </summary>
internal sealed partial class TrashPurger(
    DriveDbContext db,
    IPublishEndpoint publishEndpoint,
    DriveOptions options,
    ILogger<TrashPurger> logger) : ITrashPurger
{
    public async Task<int> PurgeAsync(DriveItem item, CancellationToken ct)
    {
        string subtree = item.Path + "%";
        int total = 0;
        while (true)
        {
            int purged = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                List<Guid> ids = await db.Items
                    .Where(x => EF.Functions.Like(x.Path, subtree))
                    .OrderByDescending(x => x.Depth).ThenBy(x => x.Id)
                    .Select(x => x.Id)
                    .Take(options.PurgeBatchSize)
                    .ToListAsync(ct);
                if (ids.Count == 0)
                {
                    return 0;
                }

                List<Guid> blobIds = await db.FileVersions.Where(v => ids.Contains(v.ItemId)).Select(v => v.BlobId).ToListAsync(ct);
                await db.Items.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct); // version, sao, nhật ký truy cập: cascade

                await publishEndpoint.Publish(new ItemsPurged(item.OwnerId, ids, blobIds), ct);
                await db.SaveChangesAsync(ct); // ghi bản tin outbox trong cùng transaction
                await transaction.CommitAsync(ct);
                return ids.Count;
            });

            if (purged == 0)
            {
                break;
            }

            total += purged;
        }

        if (total > 0)
        {
            LogPurged(total, item.Id);
        }

        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Permanently deleted {Count} items under {ItemId}")]
    private partial void LogPurged(int count, Guid itemId);
}

/// <summary>Ghi nhận lượt mở/tải, gộp 5 phút bằng một câu UPSERT có điều kiện (không đọc-rồi-ghi).</summary>
internal sealed class ItemAccessLog(DriveDbContext db, IPublishEndpoint publishEndpoint, TimeProvider timeProvider) : IItemAccessLog
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async Task RecordAsync(DriveItem item, Guid userId, string action, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset publishedBefore = now - Window;

        // 1 dòng bị ảnh hưởng = lần đầu, hoặc lần trước đã quá 5 phút ⇒ publish; 0 = vừa publish gần đây ⇒ bỏ qua.
        int affected = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO item_access_log (user_id, item_id, last_published_at) VALUES ({userId}, {item.Id}, {now})
            ON CONFLICT (user_id, item_id) DO UPDATE SET last_published_at = EXCLUDED.last_published_at
            WHERE item_access_log.last_published_at < {publishedBefore}
            """,
            ct);

        if (affected > 0)
        {
            await publishEndpoint.Publish(new ItemAccessed(item.Id, item.OwnerId, userId, action), ct);
        }
    }
}
