using Carter;
using Drive.Application.Collaboration;
using MediatR;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;

namespace Drive.Api.Endpoints;

public sealed record PostCommentRequest(string Body, Guid? VersionId = null, Guid? ParentId = null, IReadOnlyList<Guid>? Mentions = null);

public sealed record EditCommentRequest(string Body);

public sealed record ResolveCommentRequest(bool Resolved = true);

public sealed record VersionNoteRequest(string? Note);

/// <summary>
/// Lịch sử & trao đổi của tệp (ADR-017): tin nhắn chung / góp ý phiên bản, trả lời, đánh dấu đã xử lý, ghi chú phiên bản,
/// vé vào phòng realtime (Notification hub).
/// </summary>
public sealed class CollaborationEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Collaboration").MapGroup("/api/v{version:apiVersion}/items/{itemId:guid}").HasApiVersion(1).RequireAuthorization();

        api.MapGet("/comments", async (Guid itemId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new ListCommentsQuery(itemId), ct)).ToHttpResult()).WithName("ListComments");

        api.MapPost("/comments", async (Guid itemId, PostCommentRequest request, ISender sender, CancellationToken ct) =>
        {
            Result<CommentDto> result = await sender.Send(
                new PostCommentCommand(itemId, request.Body ?? string.Empty, request.VersionId, request.ParentId, request.Mentions ?? []), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/items/{itemId}/comments/{result.Value.Id}", result.Value) : result.ToProblem();
        }).WithName("PostComment");

        api.MapPatch("/comments/{commentId:guid}", async (Guid itemId, Guid commentId, EditCommentRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new EditCommentCommand(itemId, commentId, request.Body ?? string.Empty), ct)).ToHttpResult()).WithName("EditComment");

        api.MapDelete("/comments/{commentId:guid}", async (Guid itemId, Guid commentId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new DeleteCommentCommand(itemId, commentId), ct)).ToHttpResult()).WithName("DeleteComment");

        api.MapPost("/comments/{commentId:guid}/resolve", async (
            Guid itemId, Guid commentId, ResolveCommentRequest? request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new ResolveCommentCommand(itemId, commentId, request?.Resolved ?? true), ct)).ToHttpResult()).WithName("ResolveComment");

        api.MapPatch("/versions/{versionId:guid}", async (Guid itemId, Guid versionId, VersionNoteRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new UpdateVersionNoteCommand(itemId, versionId, request.Note), ct)).ToHttpResult()).WithName("UpdateVersionNote");

        api.MapPost("/live", async (Guid itemId, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            return (await sender.Send(new IssueLiveTicketQuery(itemId), ct)).ToHttpResult();
        }).WithName("IssueLiveTicket");
    }
}
