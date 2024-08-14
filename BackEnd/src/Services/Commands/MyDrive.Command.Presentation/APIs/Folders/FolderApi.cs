using Carter;
using MyDrive.Command.Presentation.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using CommandV1 = MyDrive.BuidingBlock.Contract.Services.V1.Folder;

namespace MyDrive.Command.Presentation.APIs.Folders;

public class FolderApi : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/folders";
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("folders")
            .MapGroup(BaseUrl).HasApiVersion(1);//.RequireAuthorization();

        group1.MapPost(string.Empty, CreateFoldersV1);
        group1.MapDelete("{folderId}", DeleteFoldersV1);
        group1.MapPut("{folderId}", UpdateFoldersV1);
    }

    #region ====== version 1 ======

    public static async Task<IResult> CreateFoldersV1(ISender sender, [FromBody] CommandV1.Command.CreateFolderCommand CreateFolder)
    {
        var result = await sender.Send(CreateFolder);

        if (result.IsFailure)
            return HandlerFailure(result);

        return Results.Ok(result);
    }

    public static async Task<IResult> DeleteFoldersV1(ISender sender, Guid folderId)
    {
        var result = await sender.Send(new CommandV1.Command.DeleteFolderCommand(folderId));
        return Results.Ok(result);
    }

    public static async Task<IResult> UpdateFoldersV1(ISender sender, Guid FolderId, [FromBody] CommandV1.Command.UpdateFolderCommand updateFolder)
    {
        var updateFolderCommand = new CommandV1.Command.UpdateFolderCommand(FolderId, updateFolder.Name, updateFolder.Description, updateFolder.parentId);
        var result = await sender.Send(updateFolderCommand);
        return Results.Ok(result);
    }

    #endregion ====== version 1 ======

}