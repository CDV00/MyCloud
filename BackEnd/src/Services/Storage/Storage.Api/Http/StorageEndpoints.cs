using Carter;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Web;
using Storage.Api.Archives;
using Storage.Api.ObjectStorage;
using Storage.Api.Uploads;

namespace Storage.Api.Http;

public sealed record SignPartsRequest(int[] PartNumbers);

public sealed record PartUrlResponse(int PartNumber, string Url);

public sealed record UploadedPartResponse(int PartNumber, string ETag, long Size);

public sealed record QuotaResponse(long LimitBytes, long UsedBytes, long ReservedBytes, long AvailableBytes);

/// <summary>
/// REST cho trình duyệt (qua Gateway): xin thêm URL part, xem part đã upload (để resume), xem dung lượng.
/// Tạo / hoàn tất / huỷ upload đi qua Drive vì Drive giữ metadata của file.
/// </summary>
public sealed class StorageEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Storage").MapGroup("/api/v{version:apiVersion}").HasApiVersion(1).RequireAuthorization();

        api.MapPost("/uploads/{sessionId:guid}/part-urls", SignPartsAsync).WithName("SignUploadParts");
        api.MapGet("/uploads/{sessionId:guid}/parts", ListPartsAsync).WithName("ListUploadedParts");
        api.MapGet("/quota", GetQuotaAsync).WithName("GetQuota");

        // Tệp zip (tải nhiều item / thư mục — ADR-015). Tạo yêu cầu đi qua Drive (kiểm tra quyền, dựng danh sách file).
        api.MapGet("/archives/{archiveId:guid}", GetArchiveAsync).WithName("GetArchive");
        api.MapPost("/archives/{archiveId:guid}/download-url", GetArchiveDownloadUrlAsync).WithName("GetArchiveDownloadUrl");
    }

    private static async Task<IResult> GetArchiveAsync(Guid archiveId, ArchiveService archives, ICurrentUser user, HttpResponse response, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        return Results.Ok(await archives.GetAsync(user.GetRequiredUserId(), archiveId, ct));
    }

    private static async Task<IResult> GetArchiveDownloadUrlAsync(
        Guid archiveId, ArchiveService archives, ICurrentUser user, HttpRequest http, CancellationToken ct)
    {
        DownloadUrl url = await archives.GetDownloadUrlAsync(user.GetRequiredUserId(), archiveId, http.GetPublicBaseUrl(), ct);
        http.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(url);
    }

    private static async Task<IResult> SignPartsAsync(
        Guid sessionId, SignPartsRequest request, UploadService uploads, ICurrentUser user, HttpRequest http, CancellationToken ct)
    {
        IReadOnlyList<SignedPart> parts = await uploads.SignPartsAsync(
            user.GetRequiredUserId(), sessionId, request.PartNumbers ?? [], http.GetPublicBaseUrl(), ct);
        return Results.Ok(parts.Select(p => new PartUrlResponse(p.PartNumber, p.Url)));
    }

    private static async Task<IResult> ListPartsAsync(Guid sessionId, UploadService uploads, ICurrentUser user, CancellationToken ct)
    {
        IReadOnlyList<UploadedPart> parts = await uploads.ListUploadedPartsAsync(user.GetRequiredUserId(), sessionId, ct);
        return Results.Ok(parts.Select(p => new UploadedPartResponse(p.PartNumber, p.ETag, p.SizeBytes)));
    }

    private static async Task<IResult> GetQuotaAsync(UploadService uploads, ICurrentUser user, CancellationToken ct)
    {
        QuotaInfo q = await uploads.GetQuotaAsync(user.GetRequiredUserId(), ct);
        return Results.Ok(new QuotaResponse(q.LimitBytes, q.UsedBytes, q.ReservedBytes, Math.Max(0, q.LimitBytes - q.UsedBytes - q.ReservedBytes)));
    }
}
