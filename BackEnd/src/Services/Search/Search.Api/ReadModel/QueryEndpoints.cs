using Carter;
using MongoDB.Driver;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Search.Api.ReadModel;

public sealed class QueryEndpoints : ICarterModule
{
    private const int MaxItems = 200;

    public sealed record FolderResponse(Guid Id, string Name, string Description, Guid? ParentId);

    public sealed record FileResponse(Guid Id, string Name, string ContentType, long SizeBytes, string Description, Guid FolderId);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Chỉ trả dữ liệu của người đang đăng nhập (item của người khác ⇒ 404).
        // Phase 3: tìm kiếm, "Gần đây", phân trang cursor; Phase 4: item được chia sẻ.
        var versioned = app.NewVersionedApi("Search");

        var folders = versioned.MapGroup("/api/v{version:apiVersion}/folders").HasApiVersion(1).RequireAuthorization();
        folders.MapGet("/", ListFoldersAsync).WithName("ListFolders");
        folders.MapGet("/{folderId:guid}", GetFolderAsync).WithName("GetFolder");

        var files = versioned.MapGroup("/api/v{version:apiVersion}/files").HasApiVersion(1).RequireAuthorization();
        files.MapGet("/", ListFilesAsync).WithName("ListFiles");
        files.MapGet("/{fileId:guid}", GetFileAsync).WithName("GetFile");
    }

    private static async Task<IResult> ListFoldersAsync(Guid? parentId, SearchDb db, ICurrentUser user, CancellationToken ct)
    {
        Guid ownerId = user.GetRequiredUserId();
        var items = await db.Folders.Find(x => x.OwnerId == ownerId && x.ParentId == parentId)
            .SortBy(x => x.Name).Limit(MaxItems).ToListAsync(ct);
        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> GetFolderAsync(Guid folderId, SearchDb db, ICurrentUser user, CancellationToken ct)
    {
        Guid ownerId = user.GetRequiredUserId();
        FolderDocument? doc = await db.Folders.Find(x => x.Id == folderId && x.OwnerId == ownerId).FirstOrDefaultAsync(ct);
        return doc is null ? Results.NotFound() : Results.Ok(ToResponse(doc));
    }

    private static async Task<IResult> ListFilesAsync(Guid? folderId, SearchDb db, ICurrentUser user, CancellationToken ct)
    {
        var f = Builders<FileDocument>.Filter;
        var filter = f.Eq(x => x.OwnerId, user.GetRequiredUserId());
        if (folderId is { } id)
        {
            filter &= f.Eq(x => x.FolderId, id);
        }

        var items = await db.Files.Find(filter).SortBy(x => x.Name).Limit(MaxItems).ToListAsync(ct);
        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> GetFileAsync(Guid fileId, SearchDb db, ICurrentUser user, CancellationToken ct)
    {
        Guid ownerId = user.GetRequiredUserId();
        FileDocument? doc = await db.Files.Find(x => x.Id == fileId && x.OwnerId == ownerId).FirstOrDefaultAsync(ct);
        return doc is null ? Results.NotFound() : Results.Ok(ToResponse(doc));
    }

    private static FolderResponse ToResponse(FolderDocument d) => new(d.Id, d.Name, d.Description, d.ParentId);

    private static FileResponse ToResponse(FileDocument d) => new(d.Id, d.Name, d.ContentType, d.SizeBytes, d.Description, d.FolderId);
}
