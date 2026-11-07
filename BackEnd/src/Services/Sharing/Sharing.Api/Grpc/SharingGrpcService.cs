using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.BuildingBlocks.Domain.Exceptions;
using MyCloud.Contracts.Sharing.V1;
using Sharing.Api.Access;
using GrpcPermission = MyCloud.Contracts.Sharing.V1.Permission;
using GrpcPrincipal = MyCloud.Contracts.Sharing.V1.Principal;
using GrpcRole = MyCloud.Contracts.Sharing.V1.Role;
using Role = Sharing.Api.Data.Role;

namespace Sharing.Api.Grpc;

/// <summary>
/// gRPC nội bộ (docs/03 §5). Chỉ service được gọi (Drive, bằng client credentials): Drive đã xác thực người dùng /
/// link và gửi kèm chủ sở hữu + tổ tiên của item, Sharing quyết định vai trò.
/// </summary>
[Authorize(Policy = ServiceIdentity.Policy)]
public sealed class SharingGrpcService(AccessService access) : SharingService.SharingServiceBase
{
    public override async Task<CheckAccessResponse> CheckAccess(CheckAccessRequest request, ServerCallContext context) =>
        new() { Role = ToGrpc(await access.CheckAsync(ToPrincipal(request.Principal), ToContext(request.Item), context.CancellationToken)) };

    public override async Task<BatchCheckAccessResponse> BatchCheckAccess(BatchCheckAccessRequest request, ServerCallContext context)
    {
        if (request.Items.Count > 1000)
        {
            throw new BadRequestArgument("At most 1000 items per batch.");
        }

        IReadOnlyList<Role> roles = await access.BatchCheckAsync(
            ToPrincipal(request.Principal), request.Items.Select(ToContext).ToArray(), context.CancellationToken);
        var response = new BatchCheckAccessResponse();
        response.Roles.AddRange(roles.Select(ToGrpc));
        return response;
    }

    public override async Task<ListPermissionsResponse> ListPermissions(ListPermissionsRequest request, ServerCallContext context)
    {
        var response = new ListPermissionsResponse();
        response.Permissions.AddRange((await access.ListAsync(ToContext(request.Item), context.CancellationToken)).Select(ToGrpc));
        return response;
    }

    public override async Task<GrpcPermission> GrantPermission(GrantPermissionRequest request, ServerCallContext context) =>
        ToGrpc(await access.GrantAsync(
            ToContext(request.Item),
            request.ItemName,
            request.ItemKind,
            ParseId(request.UserId),
            FromGrpc(request.Role),
            ParseId(request.GrantedBy),
            request.Notify,
            request.Message,
            context.CancellationToken,
            string.IsNullOrEmpty(request.GrantedByName) ? null : request.GrantedByName,
            string.IsNullOrEmpty(request.GranteeEmail) ? null : request.GranteeEmail));

    public override async Task<RevokePermissionResponse> RevokePermission(RevokePermissionRequest request, ServerCallContext context)
    {
        await access.RevokeAsync(ParseId(request.ItemId), ParseId(request.PermissionId), ParseId(request.RevokedBy), context.CancellationToken);
        return new RevokePermissionResponse();
    }

    public override async Task<CreateLinkResponse> CreateLink(CreateLinkRequest request, ServerCallContext context)
    {
        (LinkView link, string token) = await access.CreateLinkAsync(
            ToContext(request.Item),
            FromGrpc(request.Role),
            request.ExpiresAt?.ToDateTimeOffset(),
            Empty(request.Password),
            ParseId(request.CreatedBy),
            context.CancellationToken);
        return new CreateLinkResponse { Link = ToGrpc(link), Token = token };
    }

    public override async Task<ListLinksResponse> ListLinks(ListLinksRequest request, ServerCallContext context)
    {
        var response = new ListLinksResponse();
        response.Links.AddRange((await access.ListLinksAsync(ParseId(request.ItemId), context.CancellationToken)).Select(ToGrpc));
        return response;
    }

    public override async Task<DisableLinkResponse> DisableLink(DisableLinkRequest request, ServerCallContext context)
    {
        await access.DisableLinkAsync(ParseId(request.ItemId), ParseId(request.LinkId), context.CancellationToken);
        return new DisableLinkResponse();
    }

    public override async Task<ResolveLinkResponse> ResolveLink(ResolveLinkRequest request, ServerCallContext context)
    {
        LinkResolution r = await access.ResolveLinkAsync(request.Token, Empty(request.Password), context.CancellationToken);
        return new ResolveLinkResponse
        {
            Found = r.Found,
            RequiresPassword = r.RequiresPassword,
            ItemId = r.Found && !r.RequiresPassword ? r.ItemId.ToString() : string.Empty,
            Role = ToGrpc(r.Role),
        };
    }

    private static Access.Principal ToPrincipal(GrpcPrincipal? p) =>
        new(
            p is not null && Guid.TryParse(p.UserId, out Guid userId) ? userId : null,
            Empty(p?.LinkToken),
            Empty(p?.LinkPassword));

    private static ItemContext ToContext(ItemRef item) =>
        new(ParseId(item.ItemId), ParseId(item.OwnerId), item.AncestorIds.Select(ParseId).ToArray());

    private static GrpcRole ToGrpc(Role role) => (GrpcRole)(int)role;

    private static Role FromGrpc(GrpcRole role) =>
        System.Enum.IsDefined(role) ? (Role)(int)role : throw new InvalidRoleException();

    private static GrpcPermission ToGrpc(PermissionView p) => new()
    {
        Id = p.Id.ToString(),
        ItemId = p.ItemId.ToString(),
        UserId = p.UserId.ToString(),
        Role = ToGrpc(p.Role),
        GrantedBy = p.GrantedBy.ToString(),
        CreatedAt = Timestamp.FromDateTimeOffset(p.CreatedAt),
        Direct = p.Direct,
    };

    private static ShareLink ToGrpc(LinkView l) => new()
    {
        Id = l.Id.ToString(),
        ItemId = l.ItemId.ToString(),
        Role = ToGrpc(l.Role),
        HasPassword = l.HasPassword,
        ExpiresAt = l.ExpiresAt is { } e ? Timestamp.FromDateTimeOffset(e) : null,
        CreatedBy = l.CreatedBy.ToString(),
        CreatedAt = Timestamp.FromDateTimeOffset(l.CreatedAt),
    };

    private static Guid ParseId(string value) =>
        Guid.TryParse(value, out Guid id) ? id : throw new BadRequestArgument($"Invalid id '{value}'.");

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed class BadRequestArgument(string message) : BadRequestException(message);
}
