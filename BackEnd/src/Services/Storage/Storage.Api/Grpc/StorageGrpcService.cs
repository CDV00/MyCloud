using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.Contracts.Storage.V1;
using Storage.Api.Uploads;

namespace Storage.Api.Grpc;

/// <summary>
/// gRPC cho Drive (docs/03 §4). Drive chuyển tiếp JWT của người dùng; <c>user_id</c> trong request phải trùng với
/// người dùng của token (phòng thủ nhiều lớp) — lỗi nghiệp vụ được ServerExceptionInterceptor đổi thành status code.
/// </summary>
public sealed class StorageGrpcService(UploadService uploads, ICurrentUser currentUser, IHttpContextAccessor http)
    : StorageService.StorageServiceBase
{
    public override async Task<CreateUploadSessionResponse> CreateUploadSession(CreateUploadSessionRequest request, ServerCallContext context)
    {
        Guid owner = Caller(request.UserId);
        Guid uploader = string.IsNullOrEmpty(request.UploaderId) ? owner : Caller(request.UploaderId);
        UploadSession session = await uploads.CreateSessionAsync(
            owner, uploader, request.SizeBytes, request.MimeType, Empty(request.PublicBaseUrl), context.CancellationToken);

        var response = new CreateUploadSessionResponse
        {
            SessionId = session.SessionId.ToString(),
            BlobId = session.BlobId.ToString(),
            PartSize = session.PartSizeBytes,
            PartCount = session.PartCount,
            ExpiresAt = Timestamp.FromDateTimeOffset(session.ExpiresAt),
        };
        response.Parts.AddRange(session.Parts.Select(p => new PresignedPart { PartNumber = p.PartNumber, Url = p.Url }));
        return response;
    }

    public override async Task<SignUploadPartsResponse> SignUploadParts(SignUploadPartsRequest request, ServerCallContext context)
    {
        IReadOnlyList<SignedPart> parts = await uploads.SignPartsAsync(
            Caller(request.UserId), ParseId(request.SessionId), request.PartNumbers.ToArray(), Empty(request.PublicBaseUrl),
            context.CancellationToken);

        var response = new SignUploadPartsResponse();
        response.Parts.AddRange(parts.Select(p => new PresignedPart { PartNumber = p.PartNumber, Url = p.Url }));
        return response;
    }

    public override async Task<CompleteUploadResponse> CompleteUpload(CompleteUploadRequest request, ServerCallContext context)
    {
        (Guid blobId, long size) = await uploads.CompleteAsync(
            Caller(request.UserId),
            ParseId(request.SessionId),
            request.Parts.Select(p => (p.PartNumber, p.Etag)).ToArray(),
            context.CancellationToken);

        return new CompleteUploadResponse { BlobId = blobId.ToString(), SizeBytes = size };
    }

    public override async Task<AbortUploadResponse> AbortUpload(AbortUploadRequest request, ServerCallContext context)
    {
        await uploads.AbortAsync(Caller(request.UserId), ParseId(request.SessionId), context.CancellationToken);
        return new AbortUploadResponse();
    }

    public override async Task<GetDownloadUrlResponse> GetDownloadUrl(GetDownloadUrlRequest request, ServerCallContext context)
    {
        DownloadUrl url = await uploads.GetDownloadUrlAsync(
            Caller(request.UserId), ParseId(request.BlobId), request.FileName, Empty(request.MimeType), request.Inline,
            Empty(request.PublicBaseUrl), context.CancellationToken);

        return new GetDownloadUrlResponse { Url = url.Url, ExpiresAt = Timestamp.FromDateTimeOffset(url.ExpiresAt) };
    }

    public override async Task<GetQuotaResponse> GetQuota(GetQuotaRequest request, ServerCallContext context)
    {
        QuotaInfo quota = await uploads.GetQuotaAsync(Caller(request.UserId), context.CancellationToken);
        return new GetQuotaResponse { LimitBytes = quota.LimitBytes, UsedBytes = quota.UsedBytes, ReservedBytes = quota.ReservedBytes };
    }

    public override async Task<CopyBlobResponse> CopyBlob(CopyBlobRequest request, ServerCallContext context)
    {
        Guid user = Caller(request.UserId);
        Guid sourceOwner = string.IsNullOrEmpty(request.SourceUserId) ? user : Caller(request.SourceUserId);
        (Guid blobId, long size) = await uploads.CopyAsync(user, sourceOwner, ParseId(request.SourceBlobId), context.CancellationToken);
        return new CopyBlobResponse { BlobId = blobId.ToString(), SizeBytes = size };
    }

    /// <summary>
    /// Token người dùng ⇒ <paramref name="userId"/> phải là chính họ. Token của service (Drive, ADR-014) ⇒ tin
    /// <paramref name="userId"/>: Drive đã kiểm tra quyền thay (vd người được chia sẻ tải file của chủ sở hữu).
    /// </summary>
    private Guid Caller(string userId)
    {
        if (!Guid.TryParse(userId, out Guid requested))
        {
            throw new InvalidUploadException("Invalid user id.");
        }

        if (http.HttpContext?.User.IsService() == true)
        {
            return requested;
        }

        return requested == currentUser.GetRequiredUserId()
            ? requested
            : throw new UnauthorizedAccessException("user_id does not match the authenticated user.");
    }

    private static Guid ParseId(string value) =>
        Guid.TryParse(value, out Guid id) ? id : throw new InvalidUploadException("Invalid id.");

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
