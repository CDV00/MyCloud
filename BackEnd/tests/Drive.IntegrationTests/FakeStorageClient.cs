using System.Collections.Concurrent;
using Drive.Application.Abstractions;
using MyCloud.BuildingBlocks.Domain.Exceptions;

namespace Drive.IntegrationTests;

/// <summary>Storage giả trong bộ nhớ: kiểm tra Drive độc lập với S3/gRPC (Storage có test tích hợp riêng).</summary>
public sealed class FakeStorageClient : IStorageClient
{
    private readonly ConcurrentDictionary<Guid, (Guid UserId, long Size, Guid BlobId)> _sessions = new();

    public long QuotaBytes { get; set; } = long.MaxValue;

    public ConcurrentBag<Guid> Aborted { get; } = [];

    public string? LastPublicBaseUrl { get; private set; }

    public Task<StorageUploadSession> CreateUploadSessionAsync(Guid userId, long sizeBytes, string mimeType, string? publicBaseUrl, CancellationToken ct)
    {
        if (sizeBytes > QuotaBytes)
        {
            throw new QuotaExceeded();
        }

        LastPublicBaseUrl = publicBaseUrl;
        Guid sessionId = Guid.CreateVersion7();
        _sessions[sessionId] = (userId, sizeBytes, Guid.CreateVersion7());
        return Task.FromResult(new StorageUploadSession(
            sessionId, _sessions[sessionId].BlobId, 8 * 1024 * 1024, 1,
            [new PresignedPart(1, $"{publicBaseUrl}/mycloud-blobs/u/{userId:N}/{sessionId:N}?partNumber=1")],
            DateTimeOffset.UtcNow.AddHours(24)));
    }

    public Task<CompletedBlob> CompleteUploadAsync(Guid userId, Guid sessionId, IReadOnlyList<CompletedPart> parts, CancellationToken ct)
    {
        (Guid owner, long size, Guid blobId) = _sessions[sessionId];
        if (owner != userId)
        {
            throw new UnauthorizedAccessException();
        }

        return Task.FromResult(new CompletedBlob(blobId, size));
    }

    public Task AbortUploadAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        Aborted.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task<DownloadLink> GetDownloadUrlAsync(
        Guid userId, Guid blobId, string fileName, string mimeType, bool inline, string? publicBaseUrl, CancellationToken ct) =>
        Task.FromResult(new DownloadLink(
            $"{publicBaseUrl}/mycloud-blobs/{blobId:N}?name={Uri.EscapeDataString(fileName)}&inline={inline}",
            DateTimeOffset.UtcNow.AddMinutes(5)));

    private sealed class QuotaExceeded() : LimitExceededException("Not enough storage space for this file.")
    {
        public override string Code => "STORAGE.QUOTA_EXCEEDED";
    }
}
