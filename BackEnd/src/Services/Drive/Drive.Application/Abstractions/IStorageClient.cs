namespace Drive.Application.Abstractions;

public sealed record PresignedPart(int PartNumber, string Url);

public sealed record StorageUploadSession(
    Guid SessionId,
    Guid BlobId,
    long PartSizeBytes,
    int PartCount,
    IReadOnlyList<PresignedPart> Parts,
    DateTimeOffset ExpiresAt);

public sealed record CompletedPart(int PartNumber, string ETag);

public sealed record CompletedBlob(Guid BlobId, long SizeBytes);

public sealed record DownloadLink(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Storage service (gRPC). Lỗi của Storage được chuyển thành exception nghiệp vụ tương ứng
/// (vd vượt quota ⇒ <c>LimitExceededException</c> ⇒ HTTP 413).
/// </summary>
public interface IStorageClient
{
    Task<StorageUploadSession> CreateUploadSessionAsync(Guid userId, long sizeBytes, string mimeType, string? publicBaseUrl, CancellationToken ct);

    Task<CompletedBlob> CompleteUploadAsync(Guid userId, Guid sessionId, IReadOnlyList<CompletedPart> parts, CancellationToken ct);

    Task AbortUploadAsync(Guid userId, Guid sessionId, CancellationToken ct);

    Task<DownloadLink> GetDownloadUrlAsync(
        Guid userId, Guid blobId, string fileName, string mimeType, bool inline, string? publicBaseUrl, CancellationToken ct);
}
