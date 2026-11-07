using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storage.Api.Data;
using Storage.Api.ObjectStorage;

namespace Storage.Api.Uploads;

public sealed record SignedPart(int PartNumber, string Url);

public sealed record UploadSession(
    Guid SessionId,
    Guid BlobId,
    long PartSizeBytes,
    int PartCount,
    IReadOnlyList<SignedPart> Parts,
    DateTimeOffset ExpiresAt);

public sealed record DownloadUrl(string Url, DateTimeOffset ExpiresAt);

public sealed record QuotaInfo(long LimitBytes, long UsedBytes, long ReservedBytes);

/// <summary>
/// Vòng đời upload: Pending (giữ chỗ quota) → Uploaded (tính quota thật) → Claimed (Drive ghi nhận).
/// Mỗi bước đổi trạng thái blob và quota trong CÙNG một transaction; gọi S3 nằm ngoài transaction,
/// thất bại giữa chừng được job dọn dẹp xử lý (ADR-006). Upload "session id" chính là blob id.
/// </summary>
public sealed partial class UploadService(
    StorageDbContext db,
    QuotaStore quotas,
    QuotaAlerts alerts,
    S3ObjectStorage s3,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
    ILogger<UploadService> logger)
{
    private readonly StorageOptions _options = options.Value;

    public async Task<UploadSession> CreateSessionAsync(
        Guid userId, Guid uploaderId, long sizeBytes, string? mimeType, string? publicBaseUrl, CancellationToken ct)
    {
        if (sizeBytes < 0)
        {
            throw new InvalidUploadException("Size must not be negative.");
        }

        if (sizeBytes > _options.MaxFileSizeBytes)
        {
            throw new FileTooLargeException(_options.MaxFileSizeBytes);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid blobId = Guid.CreateVersion7();
        long partSize = _options.PartSizeFor(sizeBytes);
        var blob = new Blob
        {
            Id = blobId,
            OwnerId = userId,
            UploaderId = uploaderId,
            Bucket = s3.Bucket,
            ObjectKey = Blob.KeyFor(userId, blobId),
            MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType.Trim(),
            ExpectedSizeBytes = sizeBytes,
            Status = BlobStatus.Pending,
            PartSizeBytes = partSize,
            PartCount = (int)Math.Max(1, (sizeBytes + partSize - 1) / partSize),
            CreatedAt = now,
            ExpiresAt = now + _options.UploadSessionTtl,
        };

        // 1) Giữ chỗ quota + ghi blob trong một transaction: không bao giờ có quota bị giữ mà không có blob để trả lại.
        await InTransactionAsync(async () =>
        {
            await quotas.EnsureAsync(db, userId, ct);
            if (!await quotas.TryReserveAsync(db, userId, sizeBytes, ct))
            {
                throw new QuotaExceededException();
            }

            db.Blobs.Add(blob);
            await db.SaveChangesAsync(ct);
        }, ct);

        // 2) Mở multipart upload. Lỗi ⇒ huỷ blob, trả quota. Tiến trình chết ngay giữa 1) và 2) ⇒ job hết hạn dọn.
        try
        {
            blob.S3UploadId = await s3.InitiateMultipartAsync(blob.ObjectKey, blob.MimeType, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Kể cả khi phía gọi huỷ (hết deadline): trả quota ngay thay vì đợi job hết hạn sau 24 h.
            if (ex is not OperationCanceledException)
            {
                LogInitiateFailed(ex, blobId);
            }

            await AbortPendingAsync(blob, CancellationToken.None);
            throw;
        }

        int initialParts = Math.Min(blob.PartCount, _options.InitialSignedParts);
        IReadOnlyList<SignedPart> parts = Sign(blob, Enumerable.Range(1, initialParts), publicBaseUrl);
        return new UploadSession(blob.Id, blob.Id, partSize, blob.PartCount, parts, blob.ExpiresAt);
    }

    public async Task<IReadOnlyList<SignedPart>> SignPartsAsync(
        Guid userId, Guid sessionId, IReadOnlyCollection<int> partNumbers, string? publicBaseUrl, CancellationToken ct)
    {
        Blob blob = await FindPendingForUploaderAsync(userId, sessionId, ct);
        if (partNumbers.Count is 0 or > 1000 || partNumbers.Any(n => n < 1 || n > blob.PartCount))
        {
            throw new InvalidUploadException($"Part numbers must be between 1 and {blob.PartCount} (at most 1000 per request).");
        }

        return Sign(blob, partNumbers.Distinct().Order(), publicBaseUrl);
    }

    /// <summary>Các part đã lên storage — client dùng để tiếp tục upload sau khi mất mạng / tải lại trang.</summary>
    public async Task<IReadOnlyList<UploadedPart>> ListUploadedPartsAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        Blob blob = await FindPendingForUploaderAsync(userId, sessionId, ct);
        return await s3.ListPartsAsync(blob.ObjectKey, blob.S3UploadId!, ct);
    }

    /// <summary>Hoàn tất upload. Idempotent: gọi lại sau khi đã thành công trả cùng kết quả.</summary>
    public async Task<(Guid BlobId, long SizeBytes)> CompleteAsync(
        Guid userId, Guid sessionId, IReadOnlyCollection<(int PartNumber, string ETag)> parts, CancellationToken ct)
    {
        Blob blob = await FindOwnedAsync(userId, sessionId, ct) ?? throw new UploadNotFoundException(sessionId);
        if (blob.Status is BlobStatus.Uploaded or BlobStatus.Claimed)
        {
            return (blob.Id, blob.SizeBytes ?? blob.ExpectedSizeBytes);
        }

        if (blob.Status != BlobStatus.Pending || blob.S3UploadId is null)
        {
            throw new UploadNotPendingException(sessionId);
        }

        if (parts.Count != blob.PartCount
            || parts.Select(p => p.PartNumber).Order().SequenceEqual(Enumerable.Range(1, blob.PartCount)) is false
            || parts.Any(p => string.IsNullOrWhiteSpace(p.ETag)))
        {
            throw new InvalidUploadException($"Expected ETags for parts 1..{blob.PartCount}.");
        }

        await s3.CompleteMultipartAsync(blob.ObjectKey, blob.S3UploadId, parts, ct);

        long actualSize = await s3.GetSizeAsync(blob.ObjectKey, ct) ?? -1;
        if (actualSize != blob.ExpectedSizeBytes)
        {
            // Không cho lách quota bằng cách khai nhỏ rồi upload lớn: xoá object, trả phần giữ chỗ.
            await s3.DeleteAsync(blob.ObjectKey, ct);
            await AbortPendingAsync(blob, ct);
            throw new SizeMismatchException(blob.ExpectedSizeBytes, actualSize);
        }

        await InTransactionAsync(async () =>
        {
            blob.Status = BlobStatus.Uploaded;
            blob.SizeBytes = actualSize;
            blob.UploadedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await quotas.CommitAsync(db, blob.OwnerId, blob.ExpectedSizeBytes, actualSize, ct);
            await alerts.CheckAsync(db, blob.OwnerId, actualSize, ct);
        }, ct);

        return (blob.Id, actualSize);
    }

    /// <summary>Huỷ upload đang dở. Idempotent; không làm gì nếu upload đã xong.</summary>
    public async Task AbortAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        Blob blob = await FindOwnedAsync(userId, sessionId, ct) ?? throw new UploadNotFoundException(sessionId);
        if (blob.Status == BlobStatus.Pending)
        {
            if (blob.S3UploadId is not null)
            {
                await s3.AbortMultipartAsync(blob.ObjectKey, blob.S3UploadId, ct);
            }

            await AbortPendingAsync(blob, ct);
        }
    }

    public async Task<DownloadUrl> GetDownloadUrlAsync(
        Guid userId, Guid blobId, string fileName, string? mimeType, bool inline, string? publicBaseUrl, CancellationToken ct,
        bool internalEndpoint = false)
    {
        // Chủ sở hữu blob = chủ sở hữu file. Phase 4 (chia sẻ): Drive kiểm tra quyền rồi gọi bằng định danh của service.
        Blob blob = await FindOwnedAsync(userId, blobId, ct) ?? throw new BlobNotFoundException(blobId);
        if (blob.Status is not (BlobStatus.Uploaded or BlobStatus.Claimed))
        {
            throw new BlobNotFoundException(blobId);
        }

        DateTimeOffset expiresAt = timeProvider.GetUtcNow() + _options.DownloadUrlTtl;
        string name = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName;
        (string contentType, bool asInline) = InlineContent.Resolve(mimeType ?? blob.MimeType, inline);
        string url = s3.PresignDownload(publicBaseUrl, blob.ObjectKey, name, contentType, asInline, expiresAt, internalEndpoint);
        return new DownloadUrl(url, expiresAt);
    }

    /// <summary>
    /// Sao chép blob phía server cho <paramref name="userId"/> (sao chép file, khôi phục version). Giữ chỗ quota trước,
    /// copy, rồi ghi nhận như một upload đã xong (Uploaded) — Drive claim bằng FileVersionAdded, không claim ⇒ GC.
    /// </summary>
    public async Task<(Guid BlobId, long SizeBytes)> CopyAsync(Guid userId, Guid sourceOwnerId, Guid sourceBlobId, CancellationToken ct)
    {
        Blob source = await FindOwnedAsync(sourceOwnerId, sourceBlobId, ct) ?? throw new BlobNotFoundException(sourceBlobId);
        if (source.Status is not (BlobStatus.Uploaded or BlobStatus.Claimed))
        {
            throw new BlobNotFoundException(sourceBlobId);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        long size = source.SizeBytes ?? source.ExpectedSizeBytes;
        Guid blobId = Guid.CreateVersion7();
        var blob = new Blob
        {
            Id = blobId,
            OwnerId = userId,
            UploaderId = userId,
            Bucket = s3.Bucket,
            ObjectKey = Blob.KeyFor(userId, blobId),
            MimeType = source.MimeType,
            ExpectedSizeBytes = size,
            Status = BlobStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now + _options.UploadSessionTtl,
        };

        await InTransactionAsync(async () =>
        {
            await quotas.EnsureAsync(db, userId, ct);
            if (!await quotas.TryReserveAsync(db, userId, size, ct))
            {
                throw new QuotaExceededException();
            }

            db.Blobs.Add(blob);
            await db.SaveChangesAsync(ct);
        }, ct);

        try
        {
            await s3.CopyAsync(source.ObjectKey, blob.ObjectKey, ct);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                LogCopyFailed(ex, sourceBlobId);
            }

            await s3.DeleteAsync(blob.ObjectKey, CancellationToken.None); // copy dở / đã xong nhưng phía gọi huỷ
            await AbortPendingAsync(blob, CancellationToken.None);
            throw;
        }

        await InTransactionAsync(async () =>
        {
            blob.Status = BlobStatus.Uploaded;
            blob.SizeBytes = size;
            blob.UploadedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await quotas.CommitAsync(db, userId, size, size, ct);
            await alerts.CheckAsync(db, userId, size, ct);
        }, ct);

        return (blob.Id, size);
    }

    public async Task<QuotaInfo> GetQuotaAsync(Guid userId, CancellationToken ct)
    {
        await quotas.EnsureAsync(db, userId, ct);
        Quota quota = await db.Quotas.AsNoTracking().SingleAsync(q => q.UserId == userId, ct);
        return new QuotaInfo(quota.LimitBytes, quota.UsedBytes, quota.ReservedBytes);
    }

    private SignedPart[] Sign(Blob blob, IEnumerable<int> partNumbers, string? publicBaseUrl)
    {
        DateTimeOffset expiresAt = timeProvider.GetUtcNow() + _options.PartUrlTtl;
        return partNumbers
            .Select(n => new SignedPart(n, s3.PresignPart(publicBaseUrl, blob.ObjectKey, blob.S3UploadId!, n, expiresAt)))
            .ToArray();
    }

    /// <summary>Ký part / xem part đã upload: chỉ người đang upload (có thể không phải chủ sở hữu thư mục).</summary>
    private async Task<Blob> FindPendingForUploaderAsync(Guid uploaderId, Guid sessionId, CancellationToken ct)
    {
        Blob blob = await db.Blobs.SingleOrDefaultAsync(b => b.Id == sessionId && b.UploaderId == uploaderId, ct)
            ?? throw new UploadNotFoundException(sessionId);
        return blob.Status == BlobStatus.Pending && blob.S3UploadId is not null
            ? blob
            : throw new UploadNotPendingException(sessionId);
    }

    // Blob của người khác ⇒ coi như không tồn tại (404), không tiết lộ id hợp lệ.
    private Task<Blob?> FindOwnedAsync(Guid userId, Guid blobId, CancellationToken ct) =>
        db.Blobs.SingleOrDefaultAsync(b => b.Id == blobId && b.OwnerId == userId, ct);

    private Task AbortPendingAsync(Blob blob, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            blob.Status = BlobStatus.Aborted;
            await db.SaveChangesAsync(ct);
            await quotas.ReleaseReservedAsync(db, blob.OwnerId, blob.ExpectedSizeBytes, ct);
        }, ct);

    private Task InTransactionAsync(Func<Task> work, CancellationToken ct) =>
        // Aspire bật retry cho Npgsql ⇒ transaction tự mở phải chạy trong execution strategy.
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await work();
            await transaction.CommitAsync(ct);
        });

    [LoggerMessage(Level = LogLevel.Error, Message = "Copying blob {BlobId} failed")]
    private partial void LogCopyFailed(Exception exception, Guid blobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Starting multipart upload for blob {BlobId} failed")]
    private partial void LogInitiateFailed(Exception exception, Guid blobId);
}
