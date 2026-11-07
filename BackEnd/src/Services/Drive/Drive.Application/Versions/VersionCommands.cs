using Drive.Application.Abstractions;
using Drive.Application.Items;
using Drive.Domain;
using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Versions;

public sealed record VersionDto(
    Guid Id,
    int VersionNo,
    long SizeBytes,
    string MimeType,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    bool IsCurrent,
    string Source,
    int? RestoredFromVersionNo,
    string? Note);

/// <summary>Lịch sử version của file, mới nhất trước. Xem được cả khi file đang ở thùng rác.</summary>
public sealed record ListVersionsQuery(Guid ItemId) : IQuery<IReadOnlyList<VersionDto>>;

internal sealed class ListVersionsQueryHandler(ItemAccess access, IFileVersionRepository versions)
    : IQueryHandler<ListVersionsQuery, IReadOnlyList<VersionDto>>
{
    public async Task<Result<IReadOnlyList<VersionDto>>> Handle(ListVersionsQuery request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        if (item.IsFolder)
        {
            throw new DriveException.NotAFile(item.Id);
        }

        IReadOnlyList<FileVersion> history = await versions.ListAsync(item.Id, cancellationToken);
        return history
            .Select(v => new VersionDto(
                v.Id, v.VersionNo, v.SizeBytes, v.MimeType, v.CreatedOnUtc, v.CreatedBy, v.Id == item.CurrentVersionId,
                v.Source.ToString().ToLowerInvariant(), v.RestoredFromVersionNo, v.Note))
            .ToArray();
    }
}

/// <summary>
/// Khôi phục version cũ = tạo version MỚI có nội dung của nó (Storage sao chép blob phía server). Lịch sử luôn tiến về
/// phía trước, không version nào bị sửa, và quy trình claim/GC blob giữ nguyên như upload (ADR-006).
/// </summary>
/// <param name="Note">Lý do khôi phục (ghi vào phiên bản mới).</param>
public sealed record RestoreVersionCommand(Guid ItemId, Guid VersionId, string? Note = null) : ICommand<ItemDto>;

internal sealed class RestoreVersionCommandHandler(
    ItemAccess access,
    IFileVersionRepository versions,
    IStorageClient storage,
    VersionRetention retention,
    ItemDtos dtos) : ICommandHandler<RestoreVersionCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(RestoreVersionCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetFileAsync(request.ItemId, ItemPermission.Edit, cancellationToken);
        FileVersion version = await versions.FindAsync(request.VersionId, cancellationToken) is { } v && v.ItemId == item.Id
            ? v
            : throw new DriveException.VersionNotFound(request.VersionId);

        if (version.Id != item.CurrentVersionId)
        {
            CompletedBlob copy = await storage.CopyBlobAsync(item.OwnerId, item.OwnerId, version.BlobId, cancellationToken);
            item.AddVersion(
                Guid.CreateVersion7(), copy.BlobId, copy.SizeBytes, version.MimeType, VersionSource.Restore, version.VersionNo, request.Note);
            await retention.ApplyAsync(item, cancellationToken);
        }

        return await dtos.ForAsync(item, cancellationToken);
    }
}

/// <summary>Xoá một version cũ (không phải version hiện tại) — trả lại dung lượng của nó.</summary>
public sealed record DeleteVersionCommand(Guid ItemId, Guid VersionId) : ICommand;

internal sealed class DeleteVersionCommandHandler(ItemAccess access, IFileVersionRepository versions)
    : ICommandHandler<DeleteVersionCommand>
{
    public async Task<Result> Handle(DeleteVersionCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetFileAsync(request.ItemId, ItemPermission.Edit, cancellationToken);
        FileVersion version = await versions.FindAsync(request.VersionId, cancellationToken) is { } v && v.ItemId == item.Id
            ? v
            : throw new DriveException.VersionNotFound(request.VersionId);

        item.DeleteVersions([version]);
        versions.Remove(version);
        return Result.Success();
    }
}

/// <summary>Sau khi thêm version: xoá version cũ nhất vượt quá <see cref="DriveOptions.MaxVersionsPerFile"/>.</summary>
public sealed class VersionRetention(IFileVersionRepository versions, DriveOptions options)
{
    public async Task ApplyAsync(DriveItem item, CancellationToken ct)
    {
        // Lịch sử đã lưu (version vừa thêm chưa lưu nên không có ở đây — nó là version hiện tại, không bao giờ bị xoá).
        IReadOnlyList<FileVersion> history = await versions.ListAsync(item.Id, ct);
        IReadOnlyList<FileVersion> excess = item.VersionsBeyondLimit(history, options.MaxVersionsPerFile);
        item.DeleteVersions(excess);
        foreach (FileVersion version in excess)
        {
            versions.Remove(version);
        }
    }
}
