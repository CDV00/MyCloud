using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MyDrive.Query.Presentation.Abstractions;

using CommandV1 = MyDrive.BuidingBlock.Contract.Services.V1.File;

namespace MyDriveQuery.Presentation.APIs.Files;

public class FileApi : ApiEndpoint, ICarterModule
{
    private const string BaseUrl = "/api/v{version:apiVersion}/files";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group1 = app.NewVersionedApi("files")
            .MapGroup(BaseUrl).HasApiVersion(1); //.RequireAuthorization();

        group1.MapGet(string.Empty, GetFilesV1);
        group1.MapGet("{fileId}", GetFilesByIdV1);
    }

    #region ====== version 1 ======

    public static async Task<IResult> GetFilesV1(ISender sender)
    {
        var result = await sender.Send(new CommandV1.Query.GetFilesQuery());
        return Results.Ok(result);
    }

    public static async Task<IResult> GetFilesByIdV1(ISender sender, Guid fileId)
    {
        var result = await sender.Send(new CommandV1.Query.GetFileByIdQuery(fileId));
        return Results.Ok(result);
    }

    #endregion ====== version 1 ======
}