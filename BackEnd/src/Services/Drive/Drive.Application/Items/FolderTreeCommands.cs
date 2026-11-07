using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Items;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Items;

public enum FolderConflict
{
    /// <summary>Thư mục cùng tên đã có ⇒ tạo thư mục mới "tên (1)".</summary>
    Rename,

    /// <summary>Thư mục cùng tên đã có ⇒ dùng luôn thư mục đó (gộp nội dung).</summary>
    Merge,
}

/// <param name="Folders">Đường dẫn đã gửi → id thư mục (mới tạo hoặc đã có).</param>
/// <param name="Merged">Đường dẫn trỏ tới thư mục <b>đã có từ trước</b> (gộp) — client kiểm tra trùng tên file trong đó.</param>
public sealed record FolderTreeDto(IReadOnlyDictionary<string, Guid> Folders, IReadOnlyList<string> Merged);

/// <summary>
/// Tải cả thư mục lên (giữ cấu trúc): tạo mọi thư mục của cây trong <b>một</b> transaction trước khi upload file.
/// <paramref name="Paths"/> là đường dẫn tương đối ("Ảnh", "Ảnh/2024"); thư mục cha còn thiếu được tạo tự động.
/// </summary>
public sealed record CreateFolderTreeCommand(Guid ParentId, IReadOnlyList<string> Paths, FolderConflict Conflict)
    : ICommand<FolderTreeDto>;

internal sealed class CreateFolderTreeCommandValidator : AbstractValidator<CreateFolderTreeCommand>
{
    public CreateFolderTreeCommandValidator()
    {
        RuleFor(x => x.ParentId).NotEmpty();
        RuleFor(x => x.Paths).NotEmpty();
        RuleForEach(x => x.Paths).NotEmpty().MaximumLength(4096);
    }
}

internal sealed class CreateFolderTreeCommandHandler(
    ItemAccess access, IDriveItemRepository items, UniqueNames uniqueNames, DriveOptions options)
    : ICommandHandler<CreateFolderTreeCommand, FolderTreeDto>
{
    private const int MaxDepth = 64;

    private sealed record Node(string Key, string Display, ItemName Name, int Depth, string? ParentKey);

    public async Task<Result<FolderTreeDto>> Handle(CreateFolderTreeCommand request, CancellationToken cancellationToken)
    {
        DriveItem target = await access.GetFolderAsync(request.ParentId, ItemPermission.Edit, cancellationToken);

        // Mỗi thư mục (kể cả cha còn thiếu) một nút; khoá theo tên đã chuẩn hoá ⇒ "Ảnh" và "ảnh" là một thư mục.
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var requested = new Dictionary<string, string>(StringComparer.Ordinal); // đường dẫn gửi lên → khoá
        foreach (string path in request.Paths.Distinct())
        {
            string[] segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0 || segments.Length > MaxDepth)
            {
                throw new DriveException.InvalidName(path);
            }

            string? parentKey = null;
            string display = string.Empty;
            foreach (string segment in segments)
            {
                ItemName name = ItemName.Create(segment);
                string key = parentKey is null ? name.Key : parentKey + "/" + name.Key;
                display = display.Length == 0 ? name.Value : display + "/" + name.Value;
                nodes.TryAdd(key, new Node(key, display, name, parentKey is null ? 0 : nodes[parentKey].Depth + 1, parentKey));
                parentKey = key;
            }

            requested[path] = parentKey!;
        }

        if (nodes.Count > options.MaxFolderTreeSize)
        {
            throw new DriveException.TooManyFolders(options.MaxFolderTreeSize);
        }

        var created = new Dictionary<string, DriveItem>(StringComparer.Ordinal);
        var existed = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string?, Node> siblings in nodes.Values.OrderBy(n => n.Depth).GroupBy(n => n.ParentKey))
        {
            DriveItem parent = siblings.Key is null ? target : created[siblings.Key];
            // Chỉ thư mục đã có từ trước mới có thể chứa item trùng tên.
            bool parentExisted = siblings.Key is null || existed.Contains(siblings.Key);
            Dictionary<string, DriveItem> current = parentExisted
                ? (await items.FindChildrenByNameKeysAsync(parent.Id, siblings.Select(n => n.Name.Key).ToArray(), cancellationToken))
                    .ToDictionary(i => i.NameKey, StringComparer.Ordinal)
                : [];

            var takenHere = new HashSet<string>(StringComparer.Ordinal);
            foreach (Node node in siblings)
            {
                if (current.TryGetValue(node.Name.Key, out DriveItem? same) && same.IsFolder && request.Conflict == FolderConflict.Merge)
                {
                    created[node.Key] = same;
                    existed.Add(node.Key);
                    continue;
                }

                ItemName name = node.Name;
                if (current.ContainsKey(name.Key) || takenHere.Contains(name.Key))
                {
                    name = await uniqueNames.ResolveAsync(parent.Id, name, isFile: false, cancellationToken);
                    for (int n = 1; takenHere.Contains(name.Key); n++)
                    {
                        name = node.Name.WithSuffix(n, keepExtension: false);
                    }
                }

                takenHere.Add(name.Key);
                var folder = DriveItem.CreateFolder(Guid.CreateVersion7(), parent, name);
                items.Add(folder);
                created[node.Key] = folder;
            }
        }

        return new FolderTreeDto(
            requested.ToDictionary(kv => kv.Key, kv => created[kv.Value].Id),
            requested.Where(kv => existed.Contains(kv.Value)).Select(kv => kv.Key).ToArray());
    }
}

public sealed record NameConflictDto(string Name, Guid ItemId, string Kind, string Role);

/// <summary>
/// Tên nào trong <paramref name="Names"/> đã có trong thư mục (chưa bị xoá) — trước khi tải lên nhiều file / thư mục,
/// client hỏi một lần rồi cho người dùng chọn thay thế (version mới) / giữ cả hai / bỏ qua.
/// </summary>
public sealed record FindNameConflictsQuery(Guid FolderId, IReadOnlyList<string> Names) : IQuery<IReadOnlyList<NameConflictDto>>;

internal sealed class FindNameConflictsQueryValidator : AbstractValidator<FindNameConflictsQuery>
{
    public FindNameConflictsQueryValidator() =>
        RuleFor(x => x.Names).NotNull().Must(n => n.Count <= 1000).WithMessage("At most 1000 names per request.");
}

internal sealed class FindNameConflictsQueryHandler(ItemAccess access, IDriveItemRepository items)
    : IQueryHandler<FindNameConflictsQuery, IReadOnlyList<NameConflictDto>>
{
    public async Task<Result<IReadOnlyList<NameConflictDto>>> Handle(FindNameConflictsQuery request, CancellationToken cancellationToken)
    {
        DriveItem folder = await access.GetFolderAsync(request.FolderId, ItemPermission.View, cancellationToken);
        Dictionary<string, string> byKey = new(StringComparer.Ordinal);
        foreach (string name in request.Names)
        {
            try
            {
                byKey.TryAdd(ItemName.Create(name).Key, name);
            }
            catch (DriveException.InvalidName)
            {
                // tên không hợp lệ không thể trùng với item nào — upload sẽ tự báo lỗi
            }
        }

        IReadOnlyList<DriveItem> existing = await items.FindChildrenByNameKeysAsync(folder.Id, byKey.Keys.ToArray(), cancellationToken);
        IReadOnlyList<AccessRole> roles = await access.RolesAsync(existing, cancellationToken);
        return existing
            .Select((item, i) => new NameConflictDto(
                byKey[item.NameKey], item.Id, item.IsFolder ? "folder" : "file", roles[i].ToString().ToLowerInvariant()))
            .ToArray();
    }
}
