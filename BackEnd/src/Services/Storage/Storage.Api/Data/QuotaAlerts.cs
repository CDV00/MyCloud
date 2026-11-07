using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.Contracts.Events.Storage;

namespace Storage.Api.Data;

/// <summary>
/// Báo khi dung lượng đã dùng vượt 80 / 90 / 100 % (event <c>QuotaThresholdReached</c> qua outbox, cùng transaction
/// với thay đổi quota). Chỉ báo ngưỡng vừa vượt qua bởi lần cộng thêm này ⇒ không báo lặp mỗi lần upload.
/// </summary>
public sealed class QuotaAlerts(IPublishEndpoint publishEndpoint)
{
    private static readonly int[] Thresholds = [100, 90, 80];

    public async Task CheckAsync(StorageDbContext db, Guid userId, long addedBytes, CancellationToken ct)
    {
        if (addedBytes <= 0)
        {
            return;
        }

        Quota? quota = await db.Quotas.AsNoTracking().SingleOrDefaultAsync(q => q.UserId == userId, ct);
        if (quota is not { LimitBytes: > 0 })
        {
            return;
        }

        long before = quota.UsedBytes - addedBytes;
        foreach (int percent in Thresholds)
        {
            long mark = quota.LimitBytes * percent / 100;
            if (before < mark && quota.UsedBytes >= mark)
            {
                await publishEndpoint.Publish(new QuotaThresholdReached(userId, percent, quota.UsedBytes, quota.LimitBytes), ct);
                await db.SaveChangesAsync(ct); // ghi bản tin outbox trong cùng transaction
                return; // chỉ ngưỡng cao nhất vừa vượt
            }
        }
    }
}
