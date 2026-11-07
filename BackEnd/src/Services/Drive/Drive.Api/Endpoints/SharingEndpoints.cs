using Carter;
using Drive.Application.Abstractions;
using Drive.Application.Items;
using Drive.Application.Sharing;
using Drive.Application.Uploads;
using Drive.Domain;
using MediatR;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;

namespace Drive.Api.Endpoints;

public sealed record ShareRequest(string Email, string Role = "viewer", bool Notify = true, string? Message = null);

public sealed record ChangeRoleRequest(string Role);

public sealed record CreateLinkRequest(string Role = "viewer", DateTimeOffset? ExpiresAt = null, string? Password = null);

/// <summary>
/// Chia sẻ (docs/03 §5): Drive là mặt tiền REST, Sharing service lưu quyền (ADR-014). Người xem thấy danh sách
/// quyền; chủ sở hữu và người chỉnh sửa mới thêm / đổi / thu hồi và quản lý link.
/// </summary>
public sealed class SharingEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Sharing").MapGroup("/api/v{version:apiVersion}/items/{itemId:guid}").HasApiVersion(1).RequireAuthorization();

        api.MapGet("/sharing", async (Guid itemId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetSharingQuery(itemId), ct)).ToHttpResult()).WithName("GetSharing");

        api.MapPost("/permissions", async (Guid itemId, ShareRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new ShareWithUserCommand(itemId, request.Email ?? string.Empty, request.Role, request.Notify, request.Message), ct))
                .ToHttpResult()).WithName("ShareItem");

        api.MapPatch("/permissions/{permissionId:guid}", async (Guid itemId, Guid permissionId, ChangeRoleRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new ChangePermissionCommand(itemId, permissionId, request.Role), ct)).ToHttpResult()).WithName("ChangePermission");

        api.MapDelete("/permissions/{permissionId:guid}", async (Guid itemId, Guid permissionId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new RevokePermissionCommand(itemId, permissionId), ct)).ToHttpResult()).WithName("RevokePermission");

        api.MapPost("/links", async (Guid itemId, CreateLinkRequest? request, ISender sender, CancellationToken ct) =>
        {
            Result<CreatedLinkDto> result = await sender.Send(
                new CreateLinkCommand(itemId, request?.Role ?? "viewer", request?.ExpiresAt, request?.Password), ct);
            if (result.IsFailure)
            {
                return result.ToProblem();
            }

            return Results.Created($"/api/v1/items/{itemId}/links/{result.Value.Link.Id}", result.Value);
        }).WithName("CreateLink");

        api.MapDelete("/links/{linkId:guid}", async (Guid itemId, Guid linkId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new DisableLinkCommand(itemId, linkId), ct)).ToHttpResult()).WithName("DisableLink");
    }
}

/// <summary>
/// Truy cập qua link chia sẻ, <b>không cần đăng nhập</b> (Gateway cho đi ẩn danh, rate limit theo IP). Mật khẩu (nếu
/// có) gửi trong header <c>X-Link-Password</c>, không đặt trên URL. Chỉ đọc: xem, duyệt thư mục con, tải về.
/// </summary>
public sealed class PublicLinkEndpoints : ICarterModule
{
    public const string PasswordHeader = "X-Link-Password";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var link = app.NewVersionedApi("PublicLinks").MapGroup("/api/v{version:apiVersion}/public/links/{token}").HasApiVersion(1).AllowAnonymous();

        link.MapGet("/", async (string token, HttpRequest http, ISender sender, LinkContext context, CancellationToken ct) =>
        {
            Use(context, token, http);
            return ItemEndpoints.ItemResult(await sender.Send(new OpenLinkQuery(), ct), http.HttpContext.Response);
        }).WithName("OpenLink");

        link.MapGet("/items/{itemId:guid}", async (string token, Guid itemId, HttpRequest http, ISender sender, LinkContext context, CancellationToken ct) =>
        {
            Use(context, token, http);
            return ItemEndpoints.ItemResult(await sender.Send(new GetItemQuery(itemId), ct), http.HttpContext.Response);
        }).WithName("GetLinkedItem");

        link.MapGet("/items/{itemId:guid}/children", async (
            string token, Guid itemId, HttpRequest http, ISender sender, LinkContext context, CancellationToken ct, string? pageToken = null) =>
        {
            Use(context, token, http);
            return (await sender.Send(new ListChildrenQuery(itemId, ChildOrder.Name, false, Paging.DefaultPageSize, pageToken), ct)).ToHttpResult();
        }).WithName("ListLinkedChildren");

        link.MapGet("/items/{itemId:guid}/ancestors", async (string token, Guid itemId, HttpRequest http, ISender sender, LinkContext context, CancellationToken ct) =>
        {
            Use(context, token, http);
            return (await sender.Send(new GetAncestorsQuery(itemId), ct)).ToHttpResult();
        }).WithName("GetLinkedAncestors");

        link.MapPost("/items/{itemId:guid}/download-url", async (
            string token, Guid itemId, DownloadUrlRequest? request, HttpRequest http, ISender sender, LinkContext context, CancellationToken ct) =>
        {
            Use(context, token, http);
            http.HttpContext.Response.Headers.CacheControl = "no-store";
            return (await sender.Send(new CreateDownloadLinkCommand(itemId, null, request?.Inline ?? false, http.GetPublicBaseUrl()), ct)).ToHttpResult();
        }).WithName("GetLinkedDownloadUrl");
    }

    private static void Use(LinkContext context, string token, HttpRequest http)
    {
        context.Token = token;
        string? password = http.Headers[PasswordHeader];
        context.Password = string.IsNullOrEmpty(password) ? null : password;
    }
}
