using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Items;
using Drive.Domain.Stars;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Items;

/// <summary>Gắn / bỏ sao (idempotent). Gắn sao cần item còn hoạt động; bỏ sao được cả khi item đã ở thùng rác.</summary>
public sealed record SetStarCommand(Guid ItemId, bool Starred) : ICommand;

internal sealed class SetStarCommandHandler(ItemAccess access, IStarRepository stars, TimeProvider timeProvider)
    : ICommandHandler<SetStarCommand>
{
    public async Task<Result> Handle(SetStarCommand request, CancellationToken cancellationToken)
    {
        if (request.Starred)
        {
            DriveItem item = await access.GetActiveAsync(request.ItemId, ItemPermission.View, cancellationToken);
            if (item.IsRoot)
            {
                throw new DriveException.RootImmutable();
            }

            await stars.AddAsync(Star.Create(access.UserId, item.Id, timeProvider.GetUtcNow()), cancellationToken);
        }
        else
        {
            DriveItem item = await access.GetIncludingTrashedAsync(request.ItemId, ItemPermission.View, cancellationToken);
            await stars.RemoveAsync(access.UserId, item.Id, cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>
/// Sao chép (docs/03 §3). File: Storage sao chép blob phía server. Thư mục: sao chép cả cây (tối đa
/// <see cref="DriveOptions.MaxCopyItems"/> item) trong một yêu cầu; lưu tất cả trong một transaction. Tên trùng ⇒ "tên (1)".
/// Blob đã sao chép nhưng lưu thất bại không được "claim" ⇒ Storage tự dọn sau 24 h (ADR-006).
/// </summary>
public sealed record CopyItemCommand(Guid ItemId, Guid? TargetParentId, string? Name) : ICommand<ItemDto>;

internal sealed class CopyItemCommandValidator : AbstractValidator<CopyItemCommand>
{
    public CopyItemCommandValidator() => RuleFor(x => x.Name).MaximumLength(ItemName.MaxLength);
}

internal sealed class CopyItemCommandHandler(
    ItemAccess access,
    IDriveItemRepository items,
    IFileVersionRepository versions,
    IStorageClient storage,
    UniqueNames uniqueNames,
    DriveOptions options,
    ItemDtos dtos) : ICommandHandler<CopyItemCommand, ItemDto>
{
    private const int ParallelCopies = 4;

    public async Task<Result<ItemDto>> Handle(CopyItemCommand request, CancellationToken cancellationToken)
    {
        DriveItem source = await access.GetActiveAsync(request.ItemId, ItemPermission.View, cancellationToken);
        if (source.IsRoot)
        {
            throw new DriveException.RootImmutable();
        }

        DriveItem target = await access.GetFolderAsync(request.TargetParentId ?? source.ParentId!.Value, ItemPermission.Edit, cancellationToken);
        ItemName name = await uniqueNames.ResolveAsync(
            target.Id, ItemName.Create(string.IsNullOrWhiteSpace(request.Name) ? source.Name : request.Name), !source.IsFolder, cancellationToken);

        IReadOnlyList<DriveItem> descendants = source.IsFolder
            ? await items.ListSubtreeAsync(source, options.MaxCopyItems + 1, cancellationToken)
            : [];
        if (descendants.Count + 1 > options.MaxCopyItems)
        {
            throw new DriveException.CopyTooLarge(options.MaxCopyItems);
        }

        DriveItem[] files = [.. descendants.Where(d => !d.IsFolder), .. source.IsFolder ? [] : new[] { source }];
        IReadOnlyDictionary<Guid, FileVersion> currentVersions = await LoadCurrentVersionsAsync(files, cancellationToken);
        // Bản sao thuộc chủ sở hữu thư mục đích (người trả dung lượng), kể cả khi nguồn là file được chia sẻ.
        await EnsureQuotaAsync(target.OwnerId, files, cancellationToken);
        IReadOnlyDictionary<Guid, CompletedBlob> blobs = await CopyBlobsAsync(target.OwnerId, files, currentVersions, cancellationToken);

        DriveItem Copy(DriveItem original, DriveItem parent, ItemName copyName)
        {
            DriveItem copy = original.IsFolder
                ? DriveItem.CreateFolder(Guid.CreateVersion7(), parent, copyName)
                : DriveItem.CreateFile(
                    Guid.CreateVersion7(), parent, copyName, Guid.CreateVersion7(),
                    blobs[original.Id].BlobId, blobs[original.Id].SizeBytes, currentVersions[original.Id].MimeType, VersionSource.Copy);
            items.Add(copy);
            return copy;
        }

        DriveItem top = Copy(source, target, name);
        var copies = new Dictionary<Guid, DriveItem> { [source.Id] = top };
        foreach (DriveItem original in descendants) // cha luôn đứng trước con
        {
            copies[original.Id] = Copy(original, copies[original.ParentId!.Value], ItemName.Create(original.Name));
        }

        return await dtos.ForAsync(top, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, FileVersion>> LoadCurrentVersionsAsync(DriveItem[] files, CancellationToken ct)
    {
        IReadOnlyList<FileVersion> found = await versions.FindManyAsync(files.Select(f => f.CurrentVersionId!.Value).ToArray(), ct);
        Dictionary<Guid, FileVersion> byId = found.ToDictionary(v => v.Id);
        return files.ToDictionary(
            f => f.Id,
            f => byId.GetValueOrDefault(f.CurrentVersionId!.Value) ?? throw new InvalidOperationException($"File {f.Id} has no current version."));
    }

    /// <summary>Báo lỗi ngay thay vì sao chép dở rồi mới hết dung lượng (Storage vẫn kiểm tra lại từng blob).</summary>
    private async Task EnsureQuotaAsync(Guid ownerId, DriveItem[] files, CancellationToken ct)
    {
        long total = files.Sum(f => f.SizeBytes);
        if (total > 0 && (await storage.GetQuotaAsync(ownerId, ct)).AvailableBytes < total)
        {
            throw new DriveException.QuotaExceeded();
        }
    }

    private async Task<IReadOnlyDictionary<Guid, CompletedBlob>> CopyBlobsAsync(
        Guid ownerId, DriveItem[] files, IReadOnlyDictionary<Guid, FileVersion> currentVersions, CancellationToken ct)
    {
        var blobs = new System.Collections.Concurrent.ConcurrentDictionary<Guid, CompletedBlob>();
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelCopies, CancellationToken = ct },
            async (file, token) => blobs[file.Id] = await storage.CopyBlobAsync(ownerId, file.OwnerId, currentVersions[file.Id].BlobId, token));
        return blobs;
    }
}
