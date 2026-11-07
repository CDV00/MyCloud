using Carter;
using Drive.Application.Files;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using MyCloud.BuildingBlocks.Web;

namespace Drive.Api.Endpoints;

public sealed class FileEndpoints : ICarterModule
{
    private const long MaxUploadBytes = 100L * 1024 * 1024;

    public sealed record UpdateFileRequest(string Name, string? Description, Guid FolderId);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versioned = app.NewVersionedApi("Files");

        // Upload: multipart/form-data, field "file" (+ "description" tuỳ chọn).
        // Tắt antiforgery vì API xác thực bằng bearer token, không dùng cookie.
        versioned.MapGroup("/api/v{version:apiVersion}/folders/{folderId:guid}/files")
            .HasApiVersion(1)
            .MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes + (1024 * 1024)))
            .WithName("UploadFile");

        var group = versioned.MapGroup("/api/v{version:apiVersion}/files").HasApiVersion(1);
        group.MapPut("/{fileId:guid}", UpdateAsync).WithName("UpdateFile");
        group.MapDelete("/{fileId:guid}", DeleteAsync).WithName("DeleteFile");
    }

    private static async Task<IResult> UploadAsync(
        Guid folderId,
        IFormFile file,
        [FromForm] string? description,
        ISender sender,
        CancellationToken ct)
    {
        await using Stream content = file.OpenReadStream();
        var command = new UploadFileCommand(folderId, file.FileName, file.ContentType, file.Length, content, description);

        var result = await sender.Send(command, ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/files/{result.Value}", new { id = result.Value })
            : result.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(Guid fileId, UpdateFileRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateFileCommand(fileId, request.Name, request.Description, request.FolderId), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteAsync(Guid fileId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteFileCommand(fileId), ct);
        return result.ToHttpResult();
    }
}
