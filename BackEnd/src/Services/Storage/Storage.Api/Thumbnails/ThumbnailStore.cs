using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storage.Api.Data;
using Storage.Api.ObjectStorage;
using Storage.Api.Uploads;

namespace Storage.Api.Thumbnails;

public sealed record ThumbnailUrls(IReadOnlyList<string> Urls, DateTimeOffset ExpiresAt);

/// <summary>
/// Thumbnail của blob (ADR-015): object <c>t/{blobId}/{size}.webp</c> do Media Worker tạo. Không tính quota, xoá cùng
/// blob (<see cref="Messaging.BlobReclaimer"/>). Key chỉ phụ thuộc blob ⇒ ký URL không cần truy vấn DB; Drive chỉ xin URL
/// cho item đã có thumbnail và người gọi được xem.
/// </summary>
public sealed class ThumbnailStore(StorageDbContext db, S3ObjectStorage s3, IOptions<StorageOptions> options, TimeProvider timeProvider)
{
    public const string ContentType = "image/webp";

    public static readonly IReadOnlyList<int> Sizes = [256, 1024];

    public static string KeyFor(Guid blobId, int size) => $"t/{blobId:N}/{size}.webp";

    public static IEnumerable<string> KeysFor(Guid blobId) => Sizes.Select(size => KeyFor(blobId, size));

    /// <summary>Kích thước hỗ trợ gần nhất (≥ yêu cầu nếu có).</summary>
    public static int Normalize(int size) => Sizes.FirstOrDefault(s => s >= size, Sizes[^1]);

    /// <returns>false nếu blob không còn (đã xoá / chưa upload xong).</returns>
    public async Task<bool> PutAsync(Guid blobId, int size, byte[] content, CancellationToken ct)
    {
        if (!Sizes.Contains(size))
        {
            throw new InvalidUploadException($"Unsupported thumbnail size {size}.");
        }

        if (content.Length is 0 or > 4 * 1024 * 1024)
        {
            throw new InvalidUploadException("Thumbnail content must be between 1 byte and 4 MB.");
        }

        bool alive = await db.Blobs.AnyAsync(
            b => b.Id == blobId && (b.Status == BlobStatus.Uploaded || b.Status == BlobStatus.Claimed), ct);
        if (!alive)
        {
            return false;
        }

        await s3.PutAsync(KeyFor(blobId, size), content, ContentType, ct);
        return true;
    }

    public ThumbnailUrls Sign(IReadOnlyList<Guid> blobIds, int size, string? publicBaseUrl)
    {
        TimeSpan ttl = options.Value.ThumbnailUrlTtl;
        DateTimeOffset expiresAt = timeProvider.GetUtcNow() + ttl;
        int normalized = Normalize(size);
        // Trình duyệt chỉ cache ít hơn hạn của URL một chút để không dùng URL vừa hết hạn.
        TimeSpan cacheFor = ttl > TimeSpan.FromMinutes(5) ? ttl - TimeSpan.FromMinutes(5) : TimeSpan.Zero;
        string[] urls = blobIds
            .Select(id => s3.PresignImage(publicBaseUrl, KeyFor(id, normalized), ContentType, expiresAt, cacheFor))
            .ToArray();
        return new ThumbnailUrls(urls, expiresAt);
    }
}
