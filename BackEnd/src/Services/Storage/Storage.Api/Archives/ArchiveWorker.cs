using System.IO.Compression;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyCloud.Contracts.Events.Storage;
using Storage.Api.Data;
using Storage.Api.ObjectStorage;

namespace Storage.Api.Archives;

/// <summary>
/// Nén các tệp zip đang chờ (ADR-015). Hàng đợi chính là bảng <c>archives</c>: mỗi worker nhận một yêu cầu bằng UPDATE
/// có điều kiện (Pending → Building) nên nhiều instance chạy song song không nén trùng. Instance chết giữa chừng ⇒ yêu cầu
/// "Building" quá <see cref="StaleAfter"/> được trả về hàng đợi (tối đa <see cref="MaxAttempts"/> lần).
/// Xong: Ready + <c>ArchiveReady</c> (outbox, cùng transaction); lỗi: Failed + <c>ArchiveFailed</c>.
/// </summary>
public sealed partial class ArchiveWorker(
    IServiceScopeFactory scopeFactory,
    ArchiveSignal signal,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
    ILogger<ArchiveWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);
    private const int MaxAttempts = 3;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, options.Value.ArchiveConcurrency).Select(_ => LoopAsync(stoppingToken)));

    private async Task LoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await TryBuildNextAsync(stoppingToken))
                {
                    await signal.WaitAsync(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogLoopFailed(ex);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <returns>true nếu đã xử lý một yêu cầu (có thể còn yêu cầu khác đang chờ).</returns>
    public async Task<bool> TryBuildNextAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        Archive? archive = await ClaimAsync(db, ct);
        if (archive is null)
        {
            return false;
        }

        var s3 = scope.ServiceProvider.GetRequiredService<S3ObjectStorage>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        try
        {
            (long size, int skipped) = await BuildAsync(db, s3, archive, ct);
            DateTimeOffset now = timeProvider.GetUtcNow();
            archive.Status = ArchiveStatus.Ready;
            archive.SizeBytes = size;
            archive.SkippedCount = skipped;
            archive.CompletedAt = now;
            archive.ExpiresAt = now + options.Value.ArchiveTtl;
            archive.S3UploadId = null;
            await publisher.Publish(
                new ArchiveReady(archive.Id, archive.UserId, archive.Name, size, archive.FileCount - skipped, skipped, archive.ExpiresAt.Value), ct);
            await db.SaveChangesAsync(ct);
            LogBuilt(archive.Id, archive.FileCount, size);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Dừng service: trả về hàng đợi để instance khác (hoặc lần chạy sau) làm lại.
            await AbortUploadAsync(s3, archive, CancellationToken.None);
            await db.Archives.Where(a => a.Id == archive.Id && a.Status == ArchiveStatus.Building)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ArchiveStatus.Pending).SetProperty(a => a.S3UploadId, (string?)null), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            LogBuildFailed(ex, archive.Id);
            await AbortUploadAsync(s3, archive, ct);
            await FailAsync(db, publisher, archive, "Không tạo được tệp zip.", ct);
        }

        return true;
    }

    /// <summary>Trả yêu cầu bị bỏ dở (instance chết) về hàng đợi; quá số lần thử ⇒ Failed. Gọi từ job dọn dẹp.</summary>
    public async Task RecoverStaleAsync(StorageDbContext db, S3ObjectStorage s3, IPublishEndpoint publisher, CancellationToken ct)
    {
        DateTimeOffset staleBefore = timeProvider.GetUtcNow() - StaleAfter;
        List<Archive> stale = await db.Archives
            .Where(a => a.Status == ArchiveStatus.Building && a.StartedAt < staleBefore)
            .Take(20).ToListAsync(ct);
        foreach (Archive archive in stale)
        {
            await AbortUploadAsync(s3, archive, ct);
            if (archive.Attempts >= MaxAttempts)
            {
                await FailAsync(db, publisher, archive, "Không tạo được tệp zip.", ct);
            }
            else
            {
                archive.Status = ArchiveStatus.Pending;
                archive.S3UploadId = null;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task<Archive?> ClaimAsync(StorageDbContext db, CancellationToken ct)
    {
        for (int i = 0; i < 3; i++)
        {
            Guid? candidate = await db.Archives.AsNoTracking()
                .Where(a => a.Status == ArchiveStatus.Pending)
                .OrderBy(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (candidate is not { } id)
            {
                return null;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            int claimed = await db.Archives
                .Where(a => a.Id == id && a.Status == ArchiveStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ArchiveStatus.Building)
                    .SetProperty(a => a.StartedAt, now)
                    .SetProperty(a => a.Attempts, a => a.Attempts + 1), ct);
            if (claimed == 1)
            {
                return await db.Archives.SingleAsync(a => a.Id == id, ct);
            }
        }

        return null; // instance khác nhanh tay hơn — lần lặp sau thử tiếp
    }

    private async Task<(long Size, int Skipped)> BuildAsync(StorageDbContext db, S3ObjectStorage s3, Archive archive, CancellationToken ct)
    {
        IReadOnlyList<ArchiveItem> items = archive.Items();
        Dictionary<Guid, string> keys = await LiveObjectKeysAsync(db, items, ct);

        string uploadId = await s3.InitiateMultipartAsync(archive.ObjectKey, "application/zip", ct);
        archive.S3UploadId = uploadId;
        await db.SaveChangesAsync(ct); // để huỷ được multipart nếu instance chết giữa chừng

        long partSize = options.Value.PartSizeFor(archive.TotalBytes + (archive.EntryCount * 1024L));
        await using var output = new MultipartUploadStream(s3, archive.ObjectKey, uploadId, partSize);
        int skipped = 0;
        DateTimeOffset timestamp = timeProvider.GetLocalNow();
        await using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ArchiveItem item in items)
            {
                if (item.IsFolder)
                {
                    zip.CreateEntry(item.Path).LastWriteTime = timestamp;
                    continue;
                }

                Stream? source = keys.TryGetValue(item.BlobId!.Value, out string? key) ? await s3.OpenReadAsync(key, ct) : null;
                if (source is null)
                {
                    skipped++; // file đã bị xoá vĩnh viễn sau khi người dùng yêu cầu
                    continue;
                }

                await using (source)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(item.Path, LevelFor(item.MimeType));
                    entry.LastWriteTime = timestamp;
                    await using Stream destination = await entry.OpenAsync(ct);
                    await source.CopyToAsync(destination, 1024 * 1024, ct);
                }
            }
        }

        await output.CompleteAsync(ct);
        return (output.BytesWritten, skipped);
    }

    /// <summary>Object key của các blob còn sống (blob đã xoá ⇒ không có trong kết quả ⇒ bỏ qua entry).</summary>
    private static async Task<Dictionary<Guid, string>> LiveObjectKeysAsync(StorageDbContext db, IReadOnlyList<ArchiveItem> items, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        foreach (Guid[] chunk in items.Where(i => !i.IsFolder).Select(i => i.BlobId!.Value).Distinct().Chunk(1000))
        {
            var found = await db.Blobs.AsNoTracking()
                .Where(b => chunk.Contains(b.Id) && (b.Status == BlobStatus.Uploaded || b.Status == BlobStatus.Claimed))
                .Select(b => new { b.Id, b.ObjectKey })
                .ToListAsync(ct);
            foreach (var blob in found)
            {
                result[blob.Id] = blob.ObjectKey;
            }
        }

        return result;
    }

    /// <summary>Ảnh / video / âm thanh / tệp nén đã được nén sẵn: nén lại chỉ tốn CPU.</summary>
    internal static CompressionLevel LevelFor(string? mimeType)
    {
        string mime = (mimeType ?? string.Empty).ToLowerInvariant();
        bool precompressed = mime.StartsWith("image/", StringComparison.Ordinal) && mime != "image/bmp" && mime != "image/svg+xml"
            || mime.StartsWith("video/", StringComparison.Ordinal)
            || mime.StartsWith("audio/", StringComparison.Ordinal) && mime != "audio/wav" && mime != "audio/x-wav"
            || mime.Contains("zip", StringComparison.Ordinal) || mime.Contains("compressed", StringComparison.Ordinal)
            || mime.Contains("x-7z", StringComparison.Ordinal) || mime.Contains("x-rar", StringComparison.Ordinal)
            || mime == "application/gzip"
            || mime.Contains("openxmlformats", StringComparison.Ordinal); // docx/xlsx/pptx là zip
        return precompressed ? CompressionLevel.NoCompression : CompressionLevel.Fastest;
    }

    private static async Task AbortUploadAsync(S3ObjectStorage s3, Archive archive, CancellationToken ct)
    {
        if (archive.S3UploadId is { } uploadId)
        {
            await s3.AbortMultipartAsync(archive.ObjectKey, uploadId, ct);
        }
    }

    private async Task FailAsync(StorageDbContext db, IPublishEndpoint publisher, Archive archive, string reason, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        int changed = await db.Archives
            .Where(a => a.Id == archive.Id && (a.Status == ArchiveStatus.Building || a.Status == ArchiveStatus.Pending))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ArchiveStatus.Failed)
                .SetProperty(a => a.Error, reason)
                .SetProperty(a => a.S3UploadId, (string?)null)
                .SetProperty(a => a.CompletedAt, timeProvider.GetUtcNow()), ct);
        if (changed == 1)
        {
            await publisher.Publish(new ArchiveFailed(archive.Id, archive.UserId, archive.Name, reason), ct);
            await db.SaveChangesAsync(ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Archive {ArchiveId} built: {FileCount} files, {SizeBytes} bytes")]
    private partial void LogBuilt(Guid archiveId, int fileCount, long sizeBytes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Archive {ArchiveId} failed")]
    private partial void LogBuildFailed(Exception exception, Guid archiveId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Archive worker loop failed")]
    private partial void LogLoopFailed(Exception exception);
}
