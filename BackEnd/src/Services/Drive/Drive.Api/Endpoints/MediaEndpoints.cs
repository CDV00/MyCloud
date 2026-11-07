using Carter;
using Drive.Application.Items;
using Drive.Application.Media;
using MediatR;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;

namespace Drive.Api.Endpoints;

public sealed record ThumbnailsRequest(IReadOnlyList<Guid> ItemIds, int Size = 256);

public sealed record CreateArchiveRequest(IReadOnlyList<Guid> ItemIds);

/// <param name="Conflict">"rename" (mặc định) | "merge".</param>
public sealed record CreateFolderTreeRequest(Guid ParentId, IReadOnlyList<string> Paths, string? Conflict = null);

public sealed record NameConflictsRequest(IReadOnlyList<string> Names);

/// <summary>Thumbnail, tải dạng zip, tải cả thư mục lên (Phase 5 — ADR-015).</summary>
public sealed class MediaEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Media").MapGroup("/api/v{version:apiVersion}").HasApiVersion(1).RequireAuthorization();

        // POST (không phải GET): danh sách id dài; URL trả về có hạn nên không cache phản hồi.
        api.MapPost("/thumbnails", GetThumbnailsAsync).WithName("GetThumbnails");
        api.MapPost("/archives", CreateArchiveAsync).WithName("CreateArchive");
        api.MapPost("/folders/tree", CreateFolderTreeAsync).WithName("CreateFolderTree");
        api.MapPost("/items/{itemId:guid}/name-conflicts", FindNameConflictsAsync).WithName("FindNameConflicts");
    }

    internal static async Task<IResult> GetThumbnailsAsync(ThumbnailsRequest request, HttpRequest http, ISender sender, CancellationToken ct)
    {
        http.HttpContext.Response.Headers.CacheControl = "no-store";
        return (await sender.Send(new GetThumbnailsQuery(request.ItemIds ?? [], request.Size, http.GetPublicBaseUrl()), ct)).ToHttpResult();
    }

    private static async Task<IResult> CreateArchiveAsync(CreateArchiveRequest request, ISender sender, CancellationToken ct)
    {
        Result<ArchiveRequestDto> result = await sender.Send(new CreateArchiveCommand(request.ItemIds ?? []), ct);
        return result.IsSuccess ? Results.Accepted($"/api/storage/v1/archives/{result.Value.ArchiveId}", result.Value) : result.ToProblem();
    }

    private static async Task<IResult> CreateFolderTreeAsync(CreateFolderTreeRequest request, ISender sender, CancellationToken ct)
    {
        FolderConflict conflict = string.Equals(request.Conflict, "merge", StringComparison.OrdinalIgnoreCase)
            ? FolderConflict.Merge
            : FolderConflict.Rename;
        return (await sender.Send(new CreateFolderTreeCommand(request.ParentId, request.Paths ?? [], conflict), ct)).ToHttpResult();
    }

    private static async Task<IResult> FindNameConflictsAsync(Guid itemId, NameConflictsRequest request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new FindNameConflictsQuery(itemId, request.Names ?? []), ct)).ToHttpResult();
}
