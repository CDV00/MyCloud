using Carter;
using MongoDB.Driver;

namespace Search.Api.ReadModel;

public sealed class QueryEndpoints : ICarterModule
{
    private const int MaxItems = 200;

    public sealed record FolderResponse(Guid Id, string Name, string Description, Guid? ParentId);

    public sealed record FileResponse(Guid Id, string Name, string ContentType, long SizeBytes, string Description, Guid FolderId);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Phase 1: RequireAuthorization + lọc theo chủ sở hữu; Phase 3: tìm kiếm, Gần đây, phân trang cursor.
        var versioned = app.NewVersionedApi("Search");

        var folders = versioned.MapGroup("/api/v{version:apiVersion}/folders").HasApiVersion(1);
        folders.MapGet("/", ListFoldersAsync).WithName("ListFolders");
        folders.MapGet("/{folderId:guid}", GetFolderAsync).WithName("GetFolder");

        var files = versioned.MapGroup("/api/v{version:apiVersion}/files").HasApiVersion(1);
        files.MapGet("/", ListFilesAsync).WithName("ListFiles");
        files.MapGet("/{fileId:guid}", GetFileAsync).WithName("GetFile");
    }

    private static async Task<IResult> ListFoldersAsync(Guid? parentId, SearchDb db, CancellationToken ct)
    {
        var items = await db.Folders.Find(x => x.ParentId == parentId)
            .SortBy(x => x.Name).Limit(MaxItems).ToListAsync(ct);
        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> GetFolderAsync(Guid folderId, SearchDb db, CancellationToken ct)
    {
        FolderDocument? doc = await db.Folders.Find(x => x.Id == folderId).FirstOrDefaultAsync(ct);
        return doc is null ? Results.NotFound() : Results.Ok(ToResponse(doc));
    }

    private static async Task<IResult> ListFilesAsync(Guid? folderId, SearchDb db, CancellationToken ct)
    {
        var filter = folderId is { } id
            ? Builders<FileDocument>.Filter.Eq(x => x.FolderId, id)
            : Builders<FileDocument>.Filter.Empty;
        var items = await db.Files.Find(filter).SortBy(x => x.Name).Limit(MaxItems).ToListAsync(ct);
        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> GetFileAsync(Guid fileId, SearchDb db, CancellationToken ct)
    {
        FileDocument? doc = await db.Files.Find(x => x.Id == fileId).FirstOrDefaultAsync(ct);
        return doc is null ? Results.NotFound() : Results.Ok(ToResponse(doc));
    }

    private static FolderResponse ToResponse(FolderDocument d) => new(d.Id, d.Name, d.Description, d.ParentId);

    private static FileResponse ToResponse(FileDocument d) => new(d.Id, d.Name, d.ContentType, d.SizeBytes, d.Description, d.FolderId);
}
