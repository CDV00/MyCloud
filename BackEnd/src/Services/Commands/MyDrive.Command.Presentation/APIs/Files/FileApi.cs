using Carter;
using MyDrive.Command.Presentation.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using CommandV1 = MyDrive.BuidingBlock.Contract.Services.V1.File;

namespace MyDrive.Command.Presentation.APIs.Files;

public class FileApi : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/files";
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("files")
            .MapGroup(BaseUrl).HasApiVersion(1);//.RequireAuthorization();

        group1.MapPost(string.Empty, CreateFoldersV1);
        group1.MapDelete("{folderId}", DeleteFoldersV1);
        group1.MapPut("{folderId}", UpdateFoldersV1);
        group1.MapPost(nameof(UploadFile)+"/{folderId}", UploadFile).DisableAntiforgery();
    }

    #region ====== version 1 ======



    public static async Task<IResult> UploadFile(ISender sender, Guid folderId, IFormFile file )
    {
        CommandV1.Command.CreateFile2Command CreateFolder = new CommandV1.Command.CreateFile2Command(folderId, file);
        var result = await sender.Send(CreateFolder);
        return Results.Ok("ddddddddd");
    }

    public static async Task<IResult> CreateFoldersV1(ISender sender, [FromBody] CommandV1.Command.CreateFileCommand CreateFolder)
    {
        var result = await sender.Send(CreateFolder);

        if (result.IsFailure)
            return HandlerFailure(result);

        return Results.Ok(result);
    }

    public static async Task<IResult> DeleteFoldersV1(ISender sender, Guid folderId)
    {
        var result = await sender.Send(new CommandV1.Command.DeleteFileCommand(folderId));
        return Results.Ok(result);
    }

    public static async Task<IResult> UpdateFoldersV1(ISender sender, Guid FolderId, [FromBody] CommandV1.Command.UpdateFileCommand updateFolder)
    {
        var updateFolderCommand = new CommandV1.Command.UpdateFileCommand(FolderId, updateFolder.Name, updateFolder.type, updateFolder.size, updateFolder.description, updateFolder.FolderId);
        var result = await sender.Send(updateFolderCommand);
        return Results.Ok(result);
    }

    #endregion ====== version 1 ======

}
