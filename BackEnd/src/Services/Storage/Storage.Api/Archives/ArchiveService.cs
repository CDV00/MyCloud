using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyCloud.BuildingBlocks.Domain.Exceptions;
using MyCloud.Contracts.Events.Storage;
using Storage.Api.Data;
using Storage.Api.ObjectStorage;
using Storage.Api.Uploads;

namespace Storage.Api.Archives;

public sealed record ArchiveInfo(
    Guid Id, string Name, string Status, int FileCount, long TotalBytes, long? SizeBytes, int SkippedCount,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string? Error)
{
    public static ArchiveInfo From(Archive a) =>
        new(a.Id, a.Name, a.Status.ToString().ToLowerInvariant(), a.FileCount, a.TotalBytes, a.SizeBytes, a.SkippedCount,
            a.CreatedAt, a.ExpiresAt, a.Error);
}

public sealed class ArchiveNotFoundException(Guid archiveId)
    : NotFoundException($"Archive '{archiveId}' was not found.")
{
    public override string Code => "STORAGE.ARCHIVE_NOT_FOUND";
}

public sealed class ArchiveNotReadyException(Guid archiveId)
    : ConflictException($"Archive '{archiveId}' is not ready for download.")
{
    public override string Code => "STORAGE.ARCHIVE_NOT_READY";
}

public sealed class ArchiveTooLargeException(string message) : LimitExceededException(message)
{
    public override string Code => "STORAGE.ARCHIVE_TOO_LARGE";
}

/// <summary>Tạo yêu cầu nén (gRPC từ Drive), xem trạng thái và tải (REST của người dùng).</summary>
public sealed class ArchiveService(
    StorageDbContext db,
    S3ObjectStorage s3,
    ArchiveSignal signal,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider)
{
    private readonly StorageOptions _options = options.Value;

    public async Task<Guid> CreateAsync(Guid userId, string name, IReadOnlyList<ArchiveItem> requested, CancellationToken ct)
    {
        if (requested.Count == 0)
        {
            throw new InvalidUploadException("An archive needs at least one entry.");
        }

        if (requested.Count > _options.MaxArchiveEntries)
        {
            throw new ArchiveTooLargeException($"An archive can contain at most {_options.MaxArchiveEntries} items.");
        }

        long total = requested.Sum(i => Math.Max(0, i.SizeBytes));
        if (total > _options.MaxArchiveBytes)
        {
            throw new ArchiveTooLargeException($"An archive can contain at most {_options.MaxArchiveBytes} bytes.");
        }

        // Đường dẫn do Drive dựng; vẫn kiểm tra để không tạo được entry "../x" (zip slip khi giải nén) hay trùng lặp.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ArchiveItem>(requested.Count);
        foreach (ArchiveItem item in requested)
        {
            string path = NormalizePath(item.Path, item.IsFolder);
            if (seen.Add(path))
            {
                items.Add(item with { Path = path });
            }
        }

        var archive = new Archive
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = NormalizeName(name),
            Status = ArchiveStatus.Pending,
            EntriesJson = Archive.Serialize(items),
            EntryCount = items.Count,
            FileCount = items.Count(i => !i.IsFolder),
            TotalBytes = total,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        archive.ObjectKey = Archive.KeyFor(userId, archive.Id);
        db.Archives.Add(archive);
        await db.SaveChangesAsync(ct);

        signal.Notify(); // worker trên instance này bắt đầu ngay; instance khác nhận qua vòng lặp định kỳ
        return archive.Id;
    }

    public async Task<ArchiveInfo> GetAsync(Guid userId, Guid archiveId, CancellationToken ct) =>
        ArchiveInfo.From(await FindOwnedAsync(userId, archiveId, ct));

    public async Task<DownloadUrl> GetDownloadUrlAsync(Guid userId, Guid archiveId, string? publicBaseUrl, CancellationToken ct)
    {
        Archive archive = await FindOwnedAsync(userId, archiveId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (archive.Status != ArchiveStatus.Ready || archive.ExpiresAt <= now)
        {
            throw new ArchiveNotReadyException(archiveId);
        }

        DateTimeOffset expiresAt = now + _options.DownloadUrlTtl;
        string url = s3.PresignDownload(publicBaseUrl, archive.ObjectKey, archive.Name, "application/zip", inline: false, expiresAt);
        return new DownloadUrl(url, expiresAt);
    }

    private async Task<Archive> FindOwnedAsync(Guid userId, Guid archiveId, CancellationToken ct) =>
        await db.Archives.AsNoTracking().SingleOrDefaultAsync(a => a.Id == archiveId && a.UserId == userId, ct)
        ?? throw new ArchiveNotFoundException(archiveId);

    internal static string NormalizePath(string path, bool isFolder)
    {
        string[] segments = (path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || s.Any(char.IsControl)) || path!.Length > 4096)
        {
            throw new InvalidUploadException($"Invalid archive entry path '{path}'.");
        }

        string joined = string.Join('/', segments);
        return isFolder ? joined + "/" : joined;
    }

    internal static string NormalizeName(string name)
    {
        string trimmed = string.Concat((name ?? string.Empty).Where(c => !char.IsControl(c) && c is not ('/' or '\\'))).Trim();
        if (trimmed.Length == 0)
        {
            trimmed = "MyCloud";
        }

        if (trimmed.Length > 200)
        {
            trimmed = trimmed[..200];
        }

        return trimmed.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".zip";
    }
}

/// <summary>Đánh thức worker nén khi có yêu cầu mới trên chính instance này (không phải đợi vòng lặp định kỳ).</summary>
public sealed class ArchiveSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    public void Notify() => _signal.Release();

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct) => await _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}
