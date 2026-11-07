using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Items;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Items;

/// <summary>Thư mục gốc "Drive của tôi"; tạo nếu chưa có (user đăng ký trước Phase 2, hoặc event UserRegistered chưa tới).</summary>
public sealed record EnsureRootCommand : ICommand<ItemDto>;

internal sealed class EnsureRootCommandHandler(IDriveItemRepository items, ICurrentUser currentUser)
    : ICommandHandler<EnsureRootCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(EnsureRootCommand request, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.GetRequiredUserId();
        DriveItem? root = await items.FindRootAsync(ownerId, cancellationToken);
        if (root is null)
        {
            root = DriveItem.CreateRoot(Guid.CreateVersion7(), ownerId);
            items.Add(root);
        }

        return ItemDto.From(root);
    }
}

public sealed record CreateFolderCommand(Guid ParentId, string Name) : ICommand<ItemDto>;

internal sealed class CreateFolderCommandValidator : AbstractValidator<CreateFolderCommand>
{
    public CreateFolderCommandValidator()
    {
        RuleFor(x => x.ParentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ItemName.MaxLength);
    }
}

internal sealed class CreateFolderCommandHandler(ItemAccess access, IDriveItemRepository items)
    : ICommandHandler<CreateFolderCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(CreateFolderCommand request, CancellationToken cancellationToken)
    {
        DriveItem parent = await access.GetFolderAsync(request.ParentId, ItemPermission.Edit, cancellationToken);
        ItemName name = ItemName.Create(request.Name);
        if (await items.NameExistsAsync(parent.Id, name.Key, exceptItemId: null, cancellationToken))
        {
            throw new DriveException.NameConflict(name.Value);
        }

        var folder = DriveItem.CreateFolder(Guid.CreateVersion7(), parent, name);
        items.Add(folder);
        return ItemDto.From(folder);
    }
}

/// <param name="ExpectedRevision">Từ header <c>If-Match</c>; null = không kiểm tra.</param>
public sealed record RenameItemCommand(Guid ItemId, string Name, long? ExpectedRevision) : ICommand<ItemDto>;

internal sealed class RenameItemCommandValidator : AbstractValidator<RenameItemCommand>
{
    public RenameItemCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(ItemName.MaxLength);
}

internal sealed class RenameItemCommandHandler(ItemAccess access, IDriveItemRepository items)
    : ICommandHandler<RenameItemCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(RenameItemCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.Edit, cancellationToken);
        ItemName name = ItemName.Create(request.Name);
        if (item.ParentId is { } parentId && await items.NameExistsAsync(parentId, name.Key, item.Id, cancellationToken))
        {
            throw new DriveException.NameConflict(name.Value);
        }

        item.Rename(name, request.ExpectedRevision);
        return ItemDto.From(item);
    }
}

public sealed record MoveItemCommand(Guid ItemId, Guid NewParentId, long? ExpectedRevision) : ICommand<ItemDto>;

internal sealed class MoveItemCommandHandler(ItemAccess access, IDriveItemRepository items)
    : ICommandHandler<MoveItemCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(MoveItemCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.Edit, cancellationToken);
        DriveItem target = await access.GetFolderAsync(request.NewParentId, ItemPermission.Edit, cancellationToken);

        if (item.ParentId != target.Id && await items.NameExistsAsync(target.Id, item.NameKey, item.Id, cancellationToken))
        {
            throw new DriveException.NameConflict(item.Name);
        }

        // Cả cây con được cập nhật path trong cùng transaction với item (1 câu UPDATE — ADR-005).
        if (item.MoveTo(target, request.ExpectedRevision) is { } move && item.IsFolder)
        {
            items.MoveSubtree(move);
        }

        return ItemDto.From(item);
    }
}
