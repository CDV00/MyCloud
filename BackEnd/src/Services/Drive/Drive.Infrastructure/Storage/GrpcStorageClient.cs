using Drive.Application.Abstractions;
using Grpc.Core;
using MyCloud.BuildingBlocks.Domain.Exceptions;
using MyCloud.BuildingBlocks.Grpc;
using MyCloud.Contracts.Storage.V1;
using AppCompletedPart = Drive.Application.Abstractions.CompletedPart;
using AppPresignedPart = Drive.Application.Abstractions.PresignedPart;
using GrpcCompletedPart = MyCloud.Contracts.Storage.V1.CompletedPart;

namespace Drive.Infrastructure.Storage;

/// <summary>
/// Gọi Storage qua gRPC (h2c nội bộ). JWT của người dùng được chuyển tiếp (ForwardAuthorizationHandler).
/// Lỗi gRPC được đổi lại thành lỗi nghiệp vụ kèm mã lỗi của Storage để client hiển thị đúng (vd vượt quota).
/// </summary>
internal sealed class GrpcStorageClient(StorageService.StorageServiceClient client) : IStorageClient
{
    // Ghép hàng trăm part có thể lâu hơn deadline mặc định 2 s.
    private static readonly TimeSpan CompleteDeadline = TimeSpan.FromSeconds(60);

    public async Task<StorageUploadSession> CreateUploadSessionAsync(
        Guid userId, long sizeBytes, string mimeType, string? publicBaseUrl, CancellationToken ct)
    {
        CreateUploadSessionResponse r = await CallAsync(() => client.CreateUploadSessionAsync(
            new CreateUploadSessionRequest
            {
                UserId = userId.ToString(),
                SizeBytes = sizeBytes,
                MimeType = mimeType,
                PublicBaseUrl = publicBaseUrl ?? string.Empty,
            },
            cancellationToken: ct).ResponseAsync);

        return new StorageUploadSession(
            Guid.Parse(r.SessionId),
            Guid.Parse(r.BlobId),
            r.PartSize,
            r.PartCount,
            r.Parts.Select(p => new AppPresignedPart(p.PartNumber, p.Url)).ToArray(),
            r.ExpiresAt.ToDateTimeOffset());
    }

    public async Task<CompletedBlob> CompleteUploadAsync(Guid userId, Guid sessionId, IReadOnlyList<AppCompletedPart> parts, CancellationToken ct)
    {
        var request = new CompleteUploadRequest { SessionId = sessionId.ToString(), UserId = userId.ToString() };
        request.Parts.AddRange(parts.Select(p => new GrpcCompletedPart { PartNumber = p.PartNumber, Etag = p.ETag }));

        CompleteUploadResponse r = await CallAsync(() => client.CompleteUploadAsync(
            request, deadline: DateTime.UtcNow + CompleteDeadline, cancellationToken: ct).ResponseAsync);
        return new CompletedBlob(Guid.Parse(r.BlobId), r.SizeBytes);
    }

    public Task AbortUploadAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        CallAsync(() => client.AbortUploadAsync(
            new AbortUploadRequest { SessionId = sessionId.ToString(), UserId = userId.ToString() }, cancellationToken: ct).ResponseAsync);

    public async Task<DownloadLink> GetDownloadUrlAsync(
        Guid userId, Guid blobId, string fileName, string mimeType, bool inline, string? publicBaseUrl, CancellationToken ct)
    {
        GetDownloadUrlResponse r = await CallAsync(() => client.GetDownloadUrlAsync(
            new GetDownloadUrlRequest
            {
                BlobId = blobId.ToString(),
                UserId = userId.ToString(),
                FileName = fileName,
                MimeType = mimeType,
                Inline = inline,
                PublicBaseUrl = publicBaseUrl ?? string.Empty,
            },
            cancellationToken: ct).ResponseAsync);
        return new DownloadLink(r.Url, r.ExpiresAt.ToDateTimeOffset());
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException ex)
        {
            throw StorageError.From(ex);
        }
    }
}

/// <summary>Lỗi do Storage trả về, giữ nguyên mã lỗi (STORAGE.*, đọc từ trailer) để client dịch.</summary>
internal static class StorageError
{
    public static Exception From(RpcException ex)
    {
        string message = ex.Status.Detail;
        string? code = ex.Trailers.GetValue(ServerExceptionInterceptor.ErrorCodeTrailer);
        return ex.StatusCode switch
        {
            StatusCode.ResourceExhausted => new StorageLimitExceeded(message, code),
            StatusCode.NotFound => new StorageNotFound(message, code),
            StatusCode.InvalidArgument => new StorageBadRequest(message, code),
            StatusCode.FailedPrecondition => new StorageConflict(message, code),
            StatusCode.PermissionDenied => new UnauthorizedAccessException(message, ex),
            _ => ex, // Unavailable / DeadlineExceeded / Internal: lỗi hạ tầng ⇒ 500, có log + trace
        };
    }

    private sealed class StorageLimitExceeded(string message, string? code) : LimitExceededException(message)
    {
        public override string? Code => code;
    }

    private sealed class StorageNotFound(string message, string? code) : NotFoundException(message)
    {
        public override string? Code => code;
    }

    private sealed class StorageBadRequest(string message, string? code) : BadRequestException(message)
    {
        public override string? Code => code;
    }

    private sealed class StorageConflict(string message, string? code) : ConflictException(message)
    {
        public override string? Code => code;
    }
}
