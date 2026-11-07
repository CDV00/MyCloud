using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Drive.Application.Items;

/// <summary>
/// Item trả về cho client. <c>Revision</c> cũng nằm trong header <c>ETag</c> (dùng với <c>If-Match</c>).
/// <c>Role</c> là vai trò của người gọi (owner / editor / commenter / viewer) ⇒ client ẩn thao tác không được phép.
/// </summary>
public sealed record ItemDto(
    Guid Id,
    Guid? ParentId,
    string Kind,
    string Name,
    string? MimeType,
    long SizeBytes,
    bool IsRoot,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    bool Starred,
    DateTimeOffset? TrashedAt,
    int VersionNo,
    Guid OwnerId,
    Guid? ModifiedBy,
    string Role,
    bool HasThumbnail)
{
    public static ItemDto From(DriveItem item, bool starred, AccessRole role) =>
        new(
            item.Id,
            item.ParentId,
            item.IsFolder ? "folder" : "file",
            item.Name,
            item.MimeType,
            item.SizeBytes,
            item.IsRoot,
            item.Revision,
            item.CreatedOnUtc,
            item.ModifiedOnUtc ?? item.CreatedOnUtc,
            starred,
            item.TrashedAt,
            item.LastVersionNo,
            item.OwnerId,
            item.ModifiedBy ?? item.CreatedBy,
            role.ToString().ToLowerInvariant(),
            item.HasThumbnail);

    public static ItemDto Owned(DriveItem item) => From(item, starred: false, AccessRole.Owner);
}

public sealed record ItemPage(IReadOnlyList<ItemDto> Items, string? NextPageToken);

/// <summary>Dựng <see cref="ItemDto"/> kèm trạng thái gắn sao và vai trò của người gọi (mỗi thứ 1 truy vấn cho cả lô).</summary>
public sealed class ItemDtos(IStarRepository stars, ICurrentUser currentUser, ItemAccess access)
{
    public async Task<ItemDto> ForAsync(DriveItem item, CancellationToken ct) => (await ForAsync([item], ct))[0];

    public async Task<IReadOnlyList<ItemDto>> ForAsync(IReadOnlyList<DriveItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return [];
        }

        IReadOnlySet<Guid> starred = currentUser.UserId is { } userId
            ? await stars.StarredAmongAsync(userId, items.Select(i => i.Id).ToArray(), ct)
            : new HashSet<Guid>();
        IReadOnlyList<AccessRole> roles = await access.RolesAsync(items, ct);
        return items.Select((item, i) => ItemDto.From(item, starred.Contains(item.Id), roles[i])).ToArray();
    }

    /// <summary>Một trang kết quả: lấy dư 1 phần tử để biết còn trang sau không.</summary>
    public async Task<ItemPage> PageAsync(IReadOnlyList<DriveItem> fetched, int offset, int pageSize, CancellationToken ct)
    {
        IReadOnlyList<ItemDto> page = await ForAsync(fetched.Take(pageSize).ToArray(), ct);
        return new ItemPage(page, fetched.Count > pageSize ? PageTokens.Encode(offset + pageSize) : null);
    }
}
