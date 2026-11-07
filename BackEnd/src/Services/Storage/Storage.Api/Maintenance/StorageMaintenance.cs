using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storage.Api.Archives;
using Storage.Api.Data;
using Storage.Api.ObjectStorage;

namespace Storage.Api.Maintenance;

/// <summary>
/// Job dọn dẹp định kỳ (docs/03 §4):
/// <list type="bullet">
/// <item>Upload quá hạn chưa hoàn tất ⇒ huỷ multipart, trả quota đã giữ.</item>
/// <item>Blob đã upload nhưng Drive không claim sau <c>UnclaimedBlobTtl</c> ⇒ xoá object, trả quota (ADR-006).</item>
/// <item>Tệp zip quá hạn ⇒ xoá object; yêu cầu nén bị bỏ dở (instance chết) ⇒ trả về hàng đợi (ADR-015).</item>
/// </list>
/// Nhiều instance chạy song song không trả quota hai lần: chỉ instance đổi được trạng thái (UPDATE có điều kiện) mới trả quota.
/// </summary>
public sealed partial class StorageMaintenance(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
    ILogger<StorageMaintenance> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.MaintenanceInterval, timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        var quotas = scope.ServiceProvider.GetRequiredService<QuotaStore>();
        var s3 = scope.ServiceProvider.GetRequiredService<S3ObjectStorage>();
        DateTimeOffset now = timeProvider.GetUtcNow();

        List<Blob> expired = await db.Blobs.AsNoTracking()
            .Where(b => b.Status == BlobStatus.Pending && b.ExpiresAt < now)
            .OrderBy(b => b.ExpiresAt).Take(BatchSize).ToListAsync(ct);
        foreach (Blob blob in expired)
        {
            if (blob.S3UploadId is not null)
            {
                await s3.AbortMultipartAsync(blob.ObjectKey, blob.S3UploadId, ct);
            }

            // Bản sao (CopyBlob) bị dừng giữa chừng có thể đã ghi object.
            await s3.DeleteAsync(blob.ObjectKey, ct);

            if (await TransitionAsync(db, blob.Id, BlobStatus.Pending, BlobStatus.Aborted, ct))
            {
                await quotas.ReleaseReservedAsync(db, blob.OwnerId, blob.ExpectedSizeBytes, ct);
                LogExpired(blob.Id);
            }
        }

        DateTimeOffset unclaimedBefore = now - options.Value.UnclaimedBlobTtl;
        List<Blob> orphans = await db.Blobs.AsNoTracking()
            .Where(b => b.Status == BlobStatus.Uploaded && b.UploadedAt < unclaimedBefore)
            .OrderBy(b => b.UploadedAt).Take(BatchSize).ToListAsync(ct);
        foreach (Blob blob in orphans)
        {
            await s3.DeleteAsync(blob.ObjectKey, ct);
            if (await TransitionAsync(db, blob.Id, BlobStatus.Uploaded, BlobStatus.Deleted, ct))
            {
                await quotas.ReleaseUsedAsync(db, blob.OwnerId, blob.SizeBytes ?? blob.ExpectedSizeBytes, ct);
                LogOrphanDeleted(blob.Id);
            }
        }

        await CleanUpArchivesAsync(scope.ServiceProvider, db, s3, now, ct);
    }

    private static async Task CleanUpArchivesAsync(
        IServiceProvider services, StorageDbContext db, S3ObjectStorage s3, DateTimeOffset now, CancellationToken ct)
    {
        List<Archive> expired = await db.Archives.AsNoTracking()
            .Where(a => a.Status == ArchiveStatus.Ready && a.ExpiresAt < now)
            .OrderBy(a => a.ExpiresAt).Take(BatchSize).ToListAsync(ct);
        foreach (Archive archive in expired)
        {
            await s3.DeleteAsync(archive.ObjectKey, ct);
            await db.Archives.Where(a => a.Id == archive.Id && a.Status == ArchiveStatus.Ready)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ArchiveStatus.Expired), ct);
        }

        // Giữ bản ghi ít ngày để người dùng bấm vào thông báo cũ vẫn thấy "đã hết hạn" thay vì "không tìm thấy".
        DateTimeOffset forgetBefore = now - TimeSpan.FromDays(7);
        await db.Archives
            .Where(a => (a.Status == ArchiveStatus.Expired || a.Status == ArchiveStatus.Failed) && a.CreatedAt < forgetBefore)
            .ExecuteDeleteAsync(ct);

        await services.GetRequiredService<ArchiveWorker>()
            .RecoverStaleAsync(db, s3, services.GetRequiredService<IPublishEndpoint>(), ct);
    }

    private static async Task<bool> TransitionAsync(StorageDbContext db, Guid blobId, BlobStatus from, BlobStatus to, CancellationToken ct) =>
        await db.Blobs.Where(b => b.Id == blobId && b.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, to), ct) == 1;

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired upload {BlobId}: aborted and quota released")]
    private partial void LogExpired(Guid blobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blob {BlobId} was never claimed by Drive: deleted and quota released")]
    private partial void LogOrphanDeleted(Guid blobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Storage maintenance run failed")]
    private partial void LogFailed(Exception exception);
}
