using Drive.Application.Abstractions;
using Drive.Application.Items;
using Drive.Domain;
using Drive.Domain.Items;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Sharing;

public sealed record PersonDto(Guid Id, string DisplayName, string Email, string? AvatarUrl);

/// <param name="InheritedFrom">Null = quyền trực tiếp trên item; có giá trị = kế thừa từ thư mục này.</param>
public sealed record PermissionDto(Guid Id, PersonDto User, string Role, InheritedFromDto? InheritedFrom);

public sealed record InheritedFromDto(Guid Id, string Name);

public sealed record LinkDto(Guid Id, string Role, bool HasPassword, DateTimeOffset? ExpiresAt, DateTimeOffset CreatedAt);

public sealed record CreatedLinkDto(LinkDto Link, string Token);

/// <summary>Hộp thoại chia sẻ: chủ sở hữu, ai có quyền (trực tiếp + kế thừa), link (chỉ người chia sẻ được mới thấy).</summary>
public sealed record SharingDto(PersonDto? Owner, string MyRole, bool CanShare, IReadOnlyList<PermissionDto> Permissions, IReadOnlyList<LinkDto> Links);

internal static class Roles
{
    public static readonly string[] Grantable = ["viewer", "commenter", "editor"];

    public static AccessRole Parse(string role) => role switch
    {
        "viewer" => AccessRole.Viewer,
        "commenter" => AccessRole.Commenter,
        "editor" => AccessRole.Editor,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static string Name(AccessRole role) => role.ToString().ToLowerInvariant();
}

public sealed record GetSharingQuery(Guid ItemId) : IQuery<SharingDto>;

internal sealed class GetSharingQueryHandler(ItemAccess access, ISharingClient sharing, IUserDirectory users, IDriveItemRepository items)
    : IQueryHandler<GetSharingQuery, SharingDto>
{
    public async Task<Result<SharingDto>> Handle(GetSharingQuery request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetActiveAsync(request.ItemId, ItemPermission.View, cancellationToken);
        AccessRole myRole = (await access.RolesAsync([item], cancellationToken))[0];
        bool canShare = myRole >= ItemAccess.Required(ItemPermission.Share);

        IReadOnlyList<SharedPermission> permissions = await sharing.ListPermissionsAsync(item, cancellationToken);
        IReadOnlyList<SharedLink> links = canShare ? await sharing.ListLinksAsync(item.Id, cancellationToken) : [];

        IReadOnlyDictionary<Guid, UserInfo> people = await users.GetManyAsync(
            permissions.Select(p => p.UserId).Append(item.OwnerId).ToHashSet(), cancellationToken);
        IReadOnlyDictionary<Guid, string> folderNames = (await items.FindManyAsync(
                permissions.Where(p => !p.Direct).Select(p => p.ItemId).ToHashSet(), cancellationToken))
            .ToDictionary(i => i.Id, i => i.Name);

        PersonDto? Person(Guid id) =>
            people.TryGetValue(id, out UserInfo? u) ? new PersonDto(u.Id, u.DisplayName, u.Email, u.AvatarUrl) : null;

        return new SharingDto(
            Person(item.OwnerId),
            Roles.Name(myRole),
            canShare,
            permissions
                .Select(p => (Permission: p, Person: Person(p.UserId)))
                .Where(x => x.Person is not null) // tài khoản đã bị xoá
                .Select(x => new PermissionDto(
                    x.Permission.Id,
                    x.Person!,
                    Roles.Name(x.Permission.Role),
                    x.Permission.Direct ? null : new InheritedFromDto(x.Permission.ItemId, folderNames.GetValueOrDefault(x.Permission.ItemId, string.Empty))))
                .ToArray(),
            links.Select(ToDto).ToArray());
    }

    internal static LinkDto ToDto(SharedLink l) => new(l.Id, Roles.Name(l.Role), l.HasPassword, l.ExpiresAt, l.CreatedAt);
}

/// <summary>Chia sẻ cho một người theo email (đã có quyền trực tiếp ⇒ đổi vai trò). Người nhận được thông báo nếu <c>Notify</c>.</summary>
public sealed record ShareWithUserCommand(Guid ItemId, string Email, string Role, bool Notify, string? Message) : ICommand<PermissionDto>;

internal sealed class ShareWithUserCommandValidator : AbstractValidator<ShareWithUserCommand>
{
    public ShareWithUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).Must(r => Roles.Grantable.Contains(r)).WithMessage("Role must be viewer, commenter or editor.");
        RuleFor(x => x.Message).MaximumLength(1000);
    }
}

internal sealed class ShareWithUserCommandHandler(ItemAccess access, ISharingClient sharing, IUserDirectory users)
    : ICommandHandler<ShareWithUserCommand, PermissionDto>
{
    public async Task<Result<PermissionDto>> Handle(ShareWithUserCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetActiveAsync(request.ItemId, ItemPermission.Share, cancellationToken);
        if (item.IsRoot)
        {
            throw new DriveException.RootImmutable();
        }

        UserInfo user = await users.FindByEmailAsync(request.Email.Trim(), cancellationToken) ?? throw new DriveException.UserNotFound();
        if (user.Id == item.OwnerId)
        {
            throw new DriveException.CannotShareWithOwner();
        }

        string? sharerName = (await users.GetManyAsync([access.UserId], cancellationToken)).GetValueOrDefault(access.UserId)?.DisplayName;
        SharedPermission granted = await sharing.GrantAsync(
            item, user, Roles.Parse(request.Role), access.UserId, sharerName, request.Notify, request.Message, cancellationToken);
        return new PermissionDto(granted.Id, new PersonDto(user.Id, user.DisplayName, user.Email, user.AvatarUrl), Roles.Name(granted.Role), null);
    }
}

public sealed record ChangePermissionCommand(Guid ItemId, Guid PermissionId, string Role) : ICommand;

internal sealed class ChangePermissionCommandValidator : AbstractValidator<ChangePermissionCommand>
{
    public ChangePermissionCommandValidator() =>
        RuleFor(x => x.Role).Must(r => Roles.Grantable.Contains(r)).WithMessage("Role must be viewer, commenter or editor.");
}

internal sealed class ChangePermissionCommandHandler(ItemAccess access, ISharingClient sharing, IUserDirectory users)
    : ICommandHandler<ChangePermissionCommand>
{
    public async Task<Result> Handle(ChangePermissionCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetActiveAsync(request.ItemId, ItemPermission.Share, cancellationToken);
        SharedPermission permission = (await sharing.ListPermissionsAsync(item, cancellationToken))
            .SingleOrDefault(p => p.Id == request.PermissionId && p.Direct)
            ?? throw new DriveException.ItemNotFound(request.PermissionId);

        UserInfo grantee = (await users.GetManyAsync([permission.UserId], cancellationToken)).GetValueOrDefault(permission.UserId)
            ?? new UserInfo(permission.UserId, string.Empty, string.Empty, null);
        await sharing.GrantAsync(item, grantee, Roles.Parse(request.Role), access.UserId, null, notify: false, message: null, cancellationToken);
        return Result.Success();
    }
}

public sealed record RevokePermissionCommand(Guid ItemId, Guid PermissionId) : ICommand;

internal sealed class RevokePermissionCommandHandler(ItemAccess access, ISharingClient sharing) : ICommandHandler<RevokePermissionCommand>
{
    public async Task<Result> Handle(RevokePermissionCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetIncludingTrashedAsync(request.ItemId, ItemPermission.Share, cancellationToken);
        await sharing.RevokeAsync(item.Id, request.PermissionId, access.UserId, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Link "Bất kỳ ai có đường liên kết" (chỉ xem). Token trả về một lần.</summary>
public sealed record CreateLinkCommand(Guid ItemId, string Role, DateTimeOffset? ExpiresAt, string? Password) : ICommand<CreatedLinkDto>;

internal sealed class CreateLinkCommandValidator : AbstractValidator<CreateLinkCommand>
{
    public CreateLinkCommandValidator()
    {
        RuleFor(x => x.Role).Must(r => r is "viewer" or "commenter").WithMessage("Links can only grant viewer or commenter access.");
        RuleFor(x => x.Password).MaximumLength(128).MinimumLength(4).When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.ExpiresAt).Must(e => e is null || e > DateTimeOffset.UtcNow).WithMessage("The expiry must be in the future.");
    }
}

internal sealed class CreateLinkCommandHandler(ItemAccess access, ISharingClient sharing) : ICommandHandler<CreateLinkCommand, CreatedLinkDto>
{
    public async Task<Result<CreatedLinkDto>> Handle(CreateLinkCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetActiveAsync(request.ItemId, ItemPermission.Share, cancellationToken);
        if (item.IsRoot)
        {
            throw new DriveException.RootImmutable();
        }

        CreatedLink created = await sharing.CreateLinkAsync(
            item, Roles.Parse(request.Role), request.ExpiresAt, request.Password, access.UserId, cancellationToken);
        return new CreatedLinkDto(GetSharingQueryHandler.ToDto(created.Link), created.Token);
    }
}

public sealed record DisableLinkCommand(Guid ItemId, Guid LinkId) : ICommand;

internal sealed class DisableLinkCommandHandler(ItemAccess access, ISharingClient sharing) : ICommandHandler<DisableLinkCommand>
{
    public async Task<Result> Handle(DisableLinkCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetIncludingTrashedAsync(request.ItemId, ItemPermission.Share, cancellationToken);
        await sharing.DisableLinkAsync(item.Id, request.LinkId, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Mở link chia sẻ (ẩn danh được): item gốc của link + vai trò. Endpoint đã đặt token vào <see cref="LinkContext"/>.</summary>
public sealed record OpenLinkQuery : IQuery<ItemDto>;

internal sealed class OpenLinkQueryHandler(LinkContext link, ISharingClient sharing, ItemAccess access, ItemDtos dtos)
    : IQueryHandler<OpenLinkQuery, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(OpenLinkQuery request, CancellationToken cancellationToken)
    {
        LinkTarget target = string.IsNullOrEmpty(link.Token)
            ? new LinkTarget(false, false, Guid.Empty, AccessRole.None)
            : await sharing.ResolveLinkAsync(link.Token, link.Password, cancellationToken);
        if (!target.Found)
        {
            throw new DriveException.LinkNotFound();
        }

        if (target.RequiresPassword)
        {
            throw new DriveException.LinkPasswordRequired();
        }

        try
        {
            return await dtos.ForAsync(await access.GetActiveAsync(target.ItemId, ItemPermission.View, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is DriveException.ItemNotFound or DriveException.ItemTrashed)
        {
            throw new DriveException.LinkNotFound(); // item đã vào thùng rác / bị xoá
        }
    }
}
