using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MyDrive.Query.Presentation.Abstractions;

using CommandV1 = MyDrive.BuidingBlock.Contract.Services.V1.Folder;

namespace MyDriveQuery.Presentation.APIs.Folders;

public class FileApi : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/folders";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("folders")
            .MapGroup(BaseUrl).HasApiVersion(1); //.RequireAuthorization();

        group1.MapGet(string.Empty, GetFoldersV1);
        group1.MapGet("{folderId}", GetFoldersByIdV1);
    }

    #region ====== version 1 ======

    public static async Task<IResult> GetFoldersV1(ISender sender)
    {
        var result = await sender.Send(new CommandV1.Query.GetFoldersQuery());
        return Results.Ok(result);
    }

    public static async Task<IResult> GetFoldersByIdV1(ISender sender, Guid folderId)
    {
        var result = await sender.Send(new CommandV1.Query.GetFolderByIdQuery(folderId));
        return Results.Ok(result);
    }

    #endregion ====== version 1 ======
}