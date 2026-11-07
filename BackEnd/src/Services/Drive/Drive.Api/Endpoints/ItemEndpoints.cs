using System.Globalization;
using Carter;
using Drive.Application.Items;
using Drive.Application.Uploads;
using Drive.Domain;
using Drive.Domain.Items;
using MediatR;
using Microsoft.Net.Http.Headers;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;

namespace Drive.Api.Endpoints;

public sealed record CreateFolderRequest(Guid ParentId, string Name);

public sealed record RenameItemRequest(string Name);

public sealed record MoveItemRequest(Guid NewParentId);

public sealed record DownloadUrlRequest(bool Inline = false);

/// <summary>Cây thư mục và file (docs/03 §3). Mọi endpoint yêu cầu đăng nhập; item của người khác ⇒ 404.</summary>
public sealed class ItemEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Items").MapGroup("/api/v{version:apiVersion}").HasApiVersion(1).RequireAuthorization();

        api.MapGet("/items/root", GetRootAsync).WithName("GetRoot");
        api.MapGet("/items/{itemId:guid}", GetItemAsync).WithName("GetItem");
        api.MapGet("/items/{itemId:guid}/children", ListChildrenAsync).WithName("ListChildren");
        api.MapGet("/items/{itemId:guid}/ancestors", GetAncestorsAsync).WithName("GetAncestors");
        api.MapPost("/folders", CreateFolderAsync).WithName("CreateFolder");
        api.MapPatch("/items/{itemId:guid}", RenameAsync).WithName("RenameItem");
        api.MapPost("/items/{itemId:guid}/move", MoveAsync).WithName("MoveItem");
        api.MapPost("/items/{itemId:guid}/download-url", GetDownloadUrlAsync).WithName("GetDownloadUrl");
    }

    private static async Task<IResult> GetRootAsync(ISender sender, HttpResponse response, CancellationToken ct)
    {
        try
        {
            return ItemResult(await sender.Send(new EnsureRootCommand(), ct), response);
        }
        catch (DriveException.RootCreatedConcurrently)
        {
            // Consumer UserRegistered vừa tạo root cùng lúc: lần này chắc chắn đọc được.
            return ItemResult(await sender.Send(new EnsureRootCommand(), ct), response);
        }
    }

    private static async Task<IResult> GetItemAsync(Guid itemId, ISender sender, HttpResponse response, CancellationToken ct) =>
        ItemResult(await sender.Send(new GetItemQuery(itemId), ct), response);

    private static async Task<IResult> ListChildrenAsync(
        Guid itemId,
        ISender sender,
        CancellationToken ct,
        string? orderBy = null,
        bool desc = false,
        int pageSize = ListChildrenQuery.DefaultPageSize,
        string? pageToken = null)
    {
        ChildOrder order = orderBy?.ToLowerInvariant() switch
        {
            "modifiedat" or "modified" => ChildOrder.ModifiedAt,
            "size" => ChildOrder.Size,
            _ => ChildOrder.Name,
        };

        Result<ItemPage> result = await sender.Send(new ListChildrenQuery(itemId, order, desc, pageSize, pageToken), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAncestorsAsync(Guid itemId, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetAncestorsQuery(itemId), ct)).ToHttpResult();

    private static async Task<IResult> CreateFolderAsync(CreateFolderRequest request, ISender sender, HttpResponse response, CancellationToken ct)
    {
        Result<ItemDto> result = await sender.Send(new CreateFolderCommand(request.ParentId, request.Name), ct);
        if (result.IsFailure)
        {
            return result.ToProblem();
        }

        SetETag(response, result.Value);
        return Results.Created($"/api/v1/items/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> RenameAsync(Guid itemId, RenameItemRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        ItemResult(await sender.Send(new RenameItemCommand(itemId, request.Name, IfMatchRevision(http)), ct), http.HttpContext.Response);

    private static async Task<IResult> MoveAsync(Guid itemId, MoveItemRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        ItemResult(await sender.Send(new MoveItemCommand(itemId, request.NewParentId, IfMatchRevision(http)), ct), http.HttpContext.Response);

    private static async Task<IResult> GetDownloadUrlAsync(
        Guid itemId, DownloadUrlRequest? request, HttpRequest http, ISender sender, CancellationToken ct)
    {
        Result<DownloadLinkDto> result = await sender.Send(
            new GetDownloadUrlQuery(itemId, request?.Inline ?? false, http.GetPublicBaseUrl()), ct);
        http.HttpContext.Response.Headers.CacheControl = "no-store";
        return result.ToHttpResult();
    }

    internal static IResult ItemResult(Result<ItemDto> result, HttpResponse response)
    {
        if (result.IsFailure)
        {
            return result.ToProblem();
        }

        SetETag(response, result.Value);
        return Results.Ok(result.Value);
    }

    private static void SetETag(HttpResponse response, ItemDto item) =>
        response.Headers.ETag = $"\"{item.Revision.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary><c>If-Match: "7"</c> ⇒ 7; không có hoặc <c>*</c> ⇒ không kiểm tra; giá trị lạ ⇒ không khớp (412).</summary>
    private static long? IfMatchRevision(HttpRequest request)
    {
        string? header = request.Headers[HeaderNames.IfMatch];
        if (string.IsNullOrWhiteSpace(header) || header.Trim() == "*")
        {
            return null;
        }

        string value = header.Trim().Trim('"');
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long revision) ? revision : -1;
    }
}
