using Carter;
using Drive.Application.Folders;
using MediatR;
using MyCloud.BuildingBlocks.Web;

namespace Drive.Api.Endpoints;

public sealed class FolderEndpoints : ICarterModule
{
    public sealed record CreateFolderRequest(string Name, string? Description, Guid? ParentId);

    public sealed record UpdateFolderRequest(string Name, string? Description, Guid? ParentId);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Phase 1: thêm .RequireAuthorization() khi Gateway và service bật JWT.
        var group = app.NewVersionedApi("Folders")
            .MapGroup("/api/v{version:apiVersion}/folders")
            .HasApiVersion(1);

        group.MapPost("/", CreateAsync).WithName("CreateFolder");
        group.MapPut("/{folderId:guid}", UpdateAsync).WithName("UpdateFolder");
        group.MapDelete("/{folderId:guid}", DeleteAsync).WithName("DeleteFolder");
    }

    private static async Task<IResult> CreateAsync(CreateFolderRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateFolderCommand(request.Name, request.Description, request.ParentId), ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/folders/{result.Value}", new { id = result.Value })
            : result.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(Guid folderId, UpdateFolderRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateFolderCommand(folderId, request.Name, request.Description, request.ParentId), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteAsync(Guid folderId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteFolderCommand(folderId), ct);
        return result.ToHttpResult();
    }
}
