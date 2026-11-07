using Drive.Application.Abstractions;
using Drive.Domain.Items;
using Drive.Infrastructure.Storage;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MyCloud.Contracts.Identity.V1;
using MyCloud.Contracts.Sharing.V1;
using AppUserInfo = Drive.Application.Abstractions.UserInfo;
using GrpcUserInfo = MyCloud.Contracts.Identity.V1.UserInfo;

namespace Drive.Infrastructure.Sharing;

/// <summary>Sharing service qua gRPC bằng định danh service (ADR-014). Luôn gửi chủ sở hữu + tổ tiên của item.</summary>
internal sealed class GrpcSharingClient(SharingService.SharingServiceClient client) : ISharingClient
{
    public async Task<IReadOnlyList<AccessRole>> BatchCheckAsync(AccessPrincipal principal, IReadOnlyList<DriveItem> items, CancellationToken ct)
    {
        var request = new BatchCheckAccessRequest { Principal = ToGrpc(principal) };
        request.Items.AddRange(items.Select(ToRef));
        BatchCheckAccessResponse response = await Call(() => client.BatchCheckAccessAsync(request, cancellationToken: ct).ResponseAsync);
        return response.Roles.Select(r => (AccessRole)(int)r).ToArray();
    }

    public async Task<IReadOnlyList<SharedPermission>> ListPermissionsAsync(DriveItem item, CancellationToken ct)
    {
        ListPermissionsResponse response = await Call(() =>
            client.ListPermissionsAsync(new ListPermissionsRequest { Item = ToRef(item) }, cancellationToken: ct).ResponseAsync);
        return response.Permissions.Select(ToApp).ToArray();
    }

    public async Task<SharedPermission> GrantAsync(
        DriveItem item, AppUserInfo grantee, AccessRole role, Guid grantedBy, string? sharerName, bool notify, string? message, CancellationToken ct) =>
        ToApp(await Call(() => client.GrantPermissionAsync(
            new GrantPermissionRequest
            {
                Item = ToRef(item),
                ItemName = item.Name,
                ItemKind = item.IsFolder ? "folder" : "file",
                UserId = grantee.Id.ToString(),
                Role = (Role)(int)role,
                GrantedBy = grantedBy.ToString(),
                Notify = notify,
                Message = message ?? string.Empty,
                GrantedByName = sharerName ?? string.Empty,
                GranteeEmail = grantee.Email,
            },
            cancellationToken: ct).ResponseAsync));

    public Task RevokeAsync(Guid itemId, Guid permissionId, Guid revokedBy, CancellationToken ct) =>
        Call(() => client.RevokePermissionAsync(
            new RevokePermissionRequest { ItemId = itemId.ToString(), PermissionId = permissionId.ToString(), RevokedBy = revokedBy.ToString() },
            cancellationToken: ct).ResponseAsync);

    public async Task<CreatedLink> CreateLinkAsync(
        DriveItem item, AccessRole role, DateTimeOffset? expiresAt, string? password, Guid createdBy, CancellationToken ct)
    {
        CreateLinkResponse response = await Call(() => client.CreateLinkAsync(
            new CreateLinkRequest
            {
                Item = ToRef(item),
                Role = (Role)(int)role,
                ExpiresAt = expiresAt is { } e ? Timestamp.FromDateTimeOffset(e) : null,
                Password = password ?? string.Empty,
                CreatedBy = createdBy.ToString(),
            },
            cancellationToken: ct).ResponseAsync);
        return new CreatedLink(ToApp(response.Link), response.Token);
    }

    public async Task<IReadOnlyList<SharedLink>> ListLinksAsync(Guid itemId, CancellationToken ct)
    {
        ListLinksResponse response = await Call(() =>
            client.ListLinksAsync(new ListLinksRequest { ItemId = itemId.ToString() }, cancellationToken: ct).ResponseAsync);
        return response.Links.Select(ToApp).ToArray();
    }

    public Task DisableLinkAsync(Guid itemId, Guid linkId, CancellationToken ct) =>
        Call(() => client.DisableLinkAsync(
            new DisableLinkRequest { ItemId = itemId.ToString(), LinkId = linkId.ToString() }, cancellationToken: ct).ResponseAsync);

    public async Task<LinkTarget> ResolveLinkAsync(string token, string? password, CancellationToken ct)
    {
        ResolveLinkResponse r = await Call(() => client.ResolveLinkAsync(
            new ResolveLinkRequest { Token = token, Password = password ?? string.Empty }, cancellationToken: ct).ResponseAsync);
        return new LinkTarget(r.Found, r.RequiresPassword, Guid.TryParse(r.ItemId, out Guid id) ? id : Guid.Empty, (AccessRole)(int)r.Role);
    }

    private static ItemRef ToRef(DriveItem item)
    {
        var reference = new ItemRef { ItemId = item.Id.ToString(), OwnerId = item.OwnerId.ToString() };
        reference.AncestorIds.AddRange(item.AncestorIds.Select(a => a.ToString()));
        return reference;
    }

    private static MyCloud.Contracts.Sharing.V1.Principal ToGrpc(AccessPrincipal p) => new()
    {
        UserId = p.UserId?.ToString() ?? string.Empty,
        LinkToken = p.LinkToken ?? string.Empty,
        LinkPassword = p.LinkPassword ?? string.Empty,
    };

    private static SharedPermission ToApp(Permission p) =>
        new(Guid.Parse(p.Id), Guid.Parse(p.ItemId), Guid.Parse(p.UserId), (AccessRole)(int)p.Role, Guid.Parse(p.GrantedBy),
            p.CreatedAt.ToDateTimeOffset(), p.Direct);

    private static SharedLink ToApp(ShareLink l) =>
        new(Guid.Parse(l.Id), Guid.Parse(l.ItemId), (AccessRole)(int)l.Role, l.HasPassword, l.ExpiresAt?.ToDateTimeOffset(),
            Guid.Parse(l.CreatedBy), l.CreatedAt.ToDateTimeOffset());

    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException ex)
        {
            throw StorageError.From(ex); // cùng cách đổi status gRPC + mã lỗi trong trailer thành lỗi nghiệp vụ
        }
    }
}

/// <summary>Vai trò của người không phải chủ sở hữu ⇒ hỏi Sharing (một lời gọi cho cả lô).</summary>
internal sealed class SharingItemAuthorizer(ISharingClient sharing) : IItemAuthorizer
{
    public Task<IReadOnlyList<AccessRole>> GetRolesAsync(AccessPrincipal principal, IReadOnlyList<DriveItem> items, CancellationToken ct) =>
        principal.UserId is null && string.IsNullOrEmpty(principal.LinkToken)
            ? Task.FromResult<IReadOnlyList<AccessRole>>(items.Select(_ => AccessRole.None).ToArray())
            : sharing.BatchCheckAsync(principal, items, ct);
}

/// <summary>Identity service qua gRPC (định danh service).</summary>
internal sealed class GrpcUserDirectory(UserService.UserServiceClient client) : IUserDirectory
{
    public async Task<AppUserInfo?> FindByEmailAsync(string email, CancellationToken ct)
    {
        FindUserByEmailResponse r = await client.FindUserByEmailAsync(new FindUserByEmailRequest { Email = email }, cancellationToken: ct);
        return r.Found ? ToApp(r.User) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, AppUserInfo>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, AppUserInfo>();
        }

        var request = new BatchGetUsersRequest();
        request.Ids.AddRange(ids.Select(id => id.ToString()));
        BatchGetUsersResponse r = await client.BatchGetUsersAsync(request, cancellationToken: ct);
        return r.Users.Select(ToApp).ToDictionary(u => u.Id);
    }

    private static AppUserInfo ToApp(GrpcUserInfo u) =>
        new(Guid.Parse(u.Id), u.Email, u.DisplayName, string.IsNullOrEmpty(u.AvatarUrl) ? null : u.AvatarUrl);
}
