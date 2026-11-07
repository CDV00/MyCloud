using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Items;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Media;

public sealed record ThumbnailDto(Guid ItemId, string Url);

public sealed record ThumbnailsDto(IReadOnlyList<ThumbnailDto> Items, DateTimeOffset? ExpiresAt);

/// <summary>
/// URL thumbnail cho một lô item (lưới thư mục, ADR-015): một lần kiểm tra quyền + một lời gọi Storage cho cả lô.
/// Item không xem được / chưa có thumbnail bị bỏ qua (không lỗi) ⇒ client hiển thị biểu tượng loại file.
/// Dùng được cả khi truy cập qua link chia sẻ (<see cref="LinkContext"/>).
/// </summary>
public sealed record GetThumbnailsQuery(IReadOnlyList<Guid> ItemIds, int Size, string? PublicBaseUrl) : IQuery<ThumbnailsDto>;

internal sealed class GetThumbnailsQueryValidator : AbstractValidator<GetThumbnailsQuery>
{
    public GetThumbnailsQueryValidator()
    {
        RuleFor(x => x.ItemIds).NotNull().Must(ids => ids.Count <= 200).WithMessage("At most 200 items per request.");
        RuleFor(x => x.Size).InclusiveBetween(1, 2048);
    }
}

internal sealed class GetThumbnailsQueryHandler(
    ItemAccess access, IDriveItemRepository items, IFileVersionRepository versions, IStorageClient storage)
    : IQueryHandler<GetThumbnailsQuery, ThumbnailsDto>
{
    public async Task<Result<ThumbnailsDto>> Handle(GetThumbnailsQuery request, CancellationToken cancellationToken)
    {
        DriveItem[] candidates = (await items.FindManyAsync(request.ItemIds.Distinct().ToArray(), cancellationToken))
            .Where(i => i.HasThumbnail)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new ThumbnailsDto([], null);
        }

        IReadOnlyList<AccessRole> roles = await access.RolesAsync(candidates, cancellationToken);
        DriveItem[] visible = candidates
            .Where((item, i) => roles[i] >= AccessRole.Viewer && (!item.IsTrashed || roles[i] == AccessRole.Owner))
            .ToArray();
        if (visible.Length == 0)
        {
            return new ThumbnailsDto([], null);
        }

        Dictionary<Guid, Guid> blobOf = (await versions.FindManyAsync(visible.Select(i => i.CurrentVersionId!.Value).ToArray(), cancellationToken))
            .ToDictionary(v => v.Id, v => v.BlobId);
        DriveItem[] withBlob = visible.Where(i => blobOf.ContainsKey(i.CurrentVersionId!.Value)).ToArray();
        ThumbnailLinks links = await storage.GetThumbnailUrlsAsync(
            withBlob.Select(i => blobOf[i.CurrentVersionId!.Value]).ToArray(), request.Size, request.PublicBaseUrl, cancellationToken);

        return new ThumbnailsDto(withBlob.Select((item, i) => new ThumbnailDto(item.Id, links.Urls[i])).ToArray(), links.ExpiresAt);
    }
}

public sealed record ArchiveRequestDto(Guid ArchiveId, string Name, int FileCount, long TotalBytes);

/// <summary>
/// Tải nhiều item / cả thư mục dạng zip (ADR-015). Drive kiểm tra quyền xem, duyệt cây con (cha trước con) và dựng
/// đường dẫn trong zip; Storage nén trong job nền rồi báo qua thông báo. Tên trùng ở cấp cao nhất ⇒ "tên (1)".
/// </summary>
public sealed record CreateArchiveCommand(IReadOnlyList<Guid> ItemIds) : ICommand<ArchiveRequestDto>;

internal sealed class CreateArchiveCommandValidator : AbstractValidator<CreateArchiveCommand>
{
    public CreateArchiveCommandValidator() =>
        RuleFor(x => x.ItemIds).NotEmpty().Must(ids => ids.Count <= 100).WithMessage("At most 100 items per archive request.");
}

internal sealed class CreateArchiveCommandHandler(
    ItemAccess access,
    IDriveItemRepository items,
    IFileVersionRepository versions,
    IStorageClient storage,
    DriveOptions options,
    TimeProvider timeProvider) : ICommandHandler<CreateArchiveCommand, ArchiveRequestDto>
{
    public async Task<Result<ArchiveRequestDto>> Handle(CreateArchiveCommand request, CancellationToken cancellationToken)
    {
        Guid userId = access.UserId;
        var selected = new List<DriveItem>();
        foreach (Guid id in request.ItemIds.Distinct())
        {
            selected.Add(await access.GetActiveAsync(id, ItemPermission.View, cancellationToken));
        }

        // (item, đường dẫn trong zip) — cha luôn đứng trước con.
        var entries = new List<(DriveItem Item, string Path)>();
        var topNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DriveItem root in selected)
        {
            string top = UniqueTopName(root, topNames);
            entries.Add((root, root.IsFolder ? top + "/" : top));
            if (!root.IsFolder)
            {
                continue;
            }

            int remaining = options.MaxArchiveItems - entries.Count;
            IReadOnlyList<DriveItem> descendants = await items.ListSubtreeAsync(root, remaining + 1, cancellationToken);
            if (descendants.Count > remaining)
            {
                throw new DriveException.ArchiveTooLarge($"A zip file can contain at most {options.MaxArchiveItems} items.");
            }

            var paths = new Dictionary<Guid, string> { [root.Id] = top };
            foreach (DriveItem d in descendants)
            {
                string path = paths[d.ParentId!.Value] + "/" + d.Name;
                paths[d.Id] = path;
                entries.Add((d, d.IsFolder ? path + "/" : path));
            }
        }

        if (entries.Count > options.MaxArchiveItems)
        {
            throw new DriveException.ArchiveTooLarge($"A zip file can contain at most {options.MaxArchiveItems} items.");
        }

        long total = entries.Where(e => !e.Item.IsFolder).Sum(e => e.Item.SizeBytes);
        if (total > options.MaxArchiveBytes)
        {
            throw new DriveException.ArchiveTooLarge($"A zip file can contain at most {options.MaxArchiveBytes} bytes.");
        }

        Dictionary<Guid, FileVersion> current = await CurrentVersionsAsync(entries.Select(e => e.Item).Where(i => !i.IsFolder), cancellationToken);
        ArchiveEntry[] archiveEntries = entries
            .Where(e => e.Item.IsFolder || current.ContainsKey(e.Item.Id))
            .Select(e => e.Item.IsFolder
                ? new ArchiveEntry(e.Path, null, 0, null)
                : new ArchiveEntry(e.Path, current[e.Item.Id].BlobId, current[e.Item.Id].SizeBytes, current[e.Item.Id].MimeType))
            .ToArray();

        string name = selected.Count == 1
            ? (selected[0].IsFolder ? selected[0].Name : Path.GetFileNameWithoutExtension(selected[0].Name)) + ".zip"
            : $"MyCloud-{timeProvider.GetLocalNow():yyyy-MM-dd-HHmm}.zip";
        Guid archiveId = await storage.CreateArchiveAsync(userId, name, archiveEntries, cancellationToken);
        return new ArchiveRequestDto(archiveId, name, archiveEntries.Count(e => e.BlobId is not null), total);
    }

    private static string UniqueTopName(DriveItem item, HashSet<string> taken)
    {
        if (taken.Add(item.Name))
        {
            return item.Name;
        }

        ItemName name = ItemName.Create(item.Name);
        for (int n = 1; ; n++)
        {
            string candidate = name.WithSuffix(n, keepExtension: !item.IsFolder).Value;
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private async Task<Dictionary<Guid, FileVersion>> CurrentVersionsAsync(IEnumerable<DriveItem> files, CancellationToken ct)
    {
        var byItem = new Dictionary<Guid, FileVersion>();
        foreach (DriveItem[] chunk in files.Where(f => f.CurrentVersionId is not null).Chunk(1000))
        {
            IReadOnlyList<FileVersion> found = await versions.FindManyAsync(chunk.Select(f => f.CurrentVersionId!.Value).ToArray(), ct);
            foreach (FileVersion v in found)
            {
                byItem[v.ItemId] = v;
            }
        }

        return byItem;
    }
}
