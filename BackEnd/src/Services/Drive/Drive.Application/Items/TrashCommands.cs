using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Items;

/// <summary>
/// Chuyển vào thùng rác (docs/05 §6): item và mọi con cháu chưa bị xoá thành một nhóm, cập nhật cùng transaction.
/// Gọi lặp trên item đã ở thùng rác không làm gì. File trong thùng rác vẫn tính dung lượng.
/// </summary>
public sealed record TrashItemCommand(Guid ItemId, long? ExpectedRevision) : ICommand<ItemDto>;

internal sealed class TrashItemCommandHandler(ItemAccess access, IDriveItemRepository items, ItemDtos dtos, TimeProvider timeProvider)
    : ICommandHandler<TrashItemCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(TrashItemCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetIncludingTrashedAsync(request.ItemId, ItemPermission.Edit, cancellationToken);
        if (item.Trash(access.UserId, timeProvider.GetUtcNow(), request.ExpectedRevision) is { } subtree)
        {
            items.TrashSubtree(subtree);
        }

        return await dtos.ForAsync(item, cancellationToken);
    }
}

/// <summary>
/// Khôi phục cả nhóm về chỗ cũ; thư mục cha đang ở thùng rác ⇒ về "Drive của tôi". Trùng tên với item đã có ⇒ tự đổi
/// thành "tên (1)" thay vì báo lỗi (người dùng không thể đổi tên item trong thùng rác trước khi khôi phục).
/// </summary>
public sealed record RestoreItemCommand(Guid ItemId) : ICommand<ItemDto>;

internal sealed class RestoreItemCommandHandler(ItemAccess access, IDriveItemRepository items, UniqueNames uniqueNames, ItemDtos dtos)
    : ICommandHandler<RestoreItemCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(RestoreItemCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetIncludingTrashedAsync(request.ItemId, ItemPermission.Edit, cancellationToken);
        if (!item.IsTrashed)
        {
            return await dtos.ForAsync(item, cancellationToken); // đã khôi phục (gọi lặp)
        }

        DriveItem? parent = item.ParentId is { } parentId ? await items.FindAsync(parentId, cancellationToken) : null;
        DriveItem target = parent is { IsTrashed: false }
            ? parent
            : await items.FindRootAsync(item.OwnerId, cancellationToken) ?? throw new DriveException.ItemNotFound(item.Id);

        ItemName name = await uniqueNames.ResolveAsync(target.Id, ItemName.Create(item.Name), isFile: !item.IsFolder, cancellationToken);
        items.RestoreSubtree(item.Restore(target, name));
        return await dtos.ForAsync(item, cancellationToken);
    }
}

/// <summary>Xoá vĩnh viễn một item đang ở thùng rác (cả cây con). Storage xoá object và trả dung lượng khi nhận event.</summary>
public sealed record PurgeItemCommand(Guid ItemId) : ICommand;

internal sealed class PurgeItemCommandHandler(ItemAccess access, ITrashPurger purger) : ICommandHandler<PurgeItemCommand>
{
    public async Task<Result> Handle(PurgeItemCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetIncludingTrashedAsync(request.ItemId, ItemPermission.Edit, cancellationToken);
        if (!item.IsTrashed)
        {
            throw new DriveException.NotInTrash(item.Id);
        }

        await purger.PurgeAsync(item, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Dọn sạch thùng rác của người gọi.</summary>
public sealed record EmptyTrashCommand : ICommand<int>;

internal sealed class EmptyTrashCommandHandler(ItemAccess access, IDriveItemRepository items, ITrashPurger purger)
    : ICommandHandler<EmptyTrashCommand, int>
{
    private const int Batch = 100;

    public async Task<Result<int>> Handle(EmptyTrashCommand request, CancellationToken cancellationToken)
    {
        int purged = 0;
        while (true)
        {
            // Luôn lấy trang đầu: các nhóm vừa xoá đã biến mất khỏi danh sách.
            IReadOnlyList<DriveItem> roots = await items.ListTrashAsync(access.UserId, 0, Batch, cancellationToken);
            if (roots.Count == 0)
            {
                return purged;
            }

            int before = purged;
            foreach (DriveItem root in roots)
            {
                // Nhóm nằm trong một thư mục vừa xoá ở vòng này đã biến mất cùng nó ⇒ 0 item.
                purged += await purger.PurgeAsync(root, cancellationToken);
            }

            if (purged == before)
            {
                return purged; // không xoá thêm được gì: tránh lặp vô hạn
            }
        }
    }
}
