using Carter;
using Drive.Application.Abstractions;
using Drive.Application.Items;
using Drive.Application.Uploads;
using MediatR;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;

namespace Drive.Api.Endpoints;

public sealed record StartUploadRequest(Guid ParentId, string Name, long SizeBytes, string? MimeType);

public sealed record CompleteUploadRequest(CompletedPart[] Parts);

/// <summary>
/// Upload trực tiếp lên object storage (docs/05, ADR-002): (1) POST /uploads lấy URL ký sẵn, (2) trình duyệt PUT từng
/// part, (3) POST /uploads/{id}/complete. Byte của file không đi qua Gateway hay service .NET.
/// </summary>
public sealed class UploadEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var uploads = app.NewVersionedApi("Uploads").MapGroup("/api/v{version:apiVersion}/uploads").HasApiVersion(1).RequireAuthorization();

        uploads.MapPost("/", StartAsync).WithName("StartUpload");
        uploads.MapPost("/{uploadId:guid}/complete", CompleteAsync).WithName("CompleteUpload");
        uploads.MapDelete("/{uploadId:guid}", AbortAsync).WithName("AbortUpload");
    }

    private static async Task<IResult> StartAsync(StartUploadRequest request, HttpRequest http, ISender sender, CancellationToken ct)
    {
        Result<UploadDto> result = await sender.Send(
            new StartUploadCommand(request.ParentId, request.Name, request.SizeBytes, request.MimeType, http.GetPublicBaseUrl()), ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/uploads/{result.Value.UploadId}", result.Value)
            : result.ToProblem();
    }

    private static async Task<IResult> CompleteAsync(
        Guid uploadId, CompleteUploadRequest request, ISender sender, HttpResponse response, CancellationToken ct) =>
        ItemEndpoints.ItemResult(await sender.Send(new CompleteUploadCommand(uploadId, request.Parts ?? []), ct), response);

    private static async Task<IResult> AbortAsync(Guid uploadId, ISender sender, CancellationToken ct) =>
        (await sender.Send(new AbortUploadCommand(uploadId), ct)).ToHttpResult();
}
