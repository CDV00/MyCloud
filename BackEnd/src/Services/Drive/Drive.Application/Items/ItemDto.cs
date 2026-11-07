using Drive.Domain;
using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Drive.Application.Items;

/// <summary>Item trả về cho client. <c>Revision</c> cũng được trả trong header <c>ETag</c> để dùng với <c>If-Match</c>.</summary>
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
    int VersionNo)
{
    public static ItemDto From(DriveItem item, bool starred) =>
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
            item.LastVersionNo);
}

public sealed record ItemPage(IReadOnlyList<ItemDto> Items, string? NextPageToken);

/// <summary>Dựng <see cref="ItemDto"/> kèm trạng thái gắn sao của người đang gọi (1 truy vấn cho cả danh sách).</summary>
public sealed class ItemDtos(IStarRepository stars, ICurrentUser currentUser)
{
    public async Task<ItemDto> ForAsync(DriveItem item, CancellationToken ct) =>
        ItemDto.From(item, currentUser.UserId is { } userId && await stars.IsStarredAsync(userId, item.Id, ct));

    public async Task<IReadOnlyList<ItemDto>> ForAsync(IReadOnlyList<DriveItem> items, CancellationToken ct)
    {
        IReadOnlySet<Guid> starred = currentUser.UserId is { } userId && items.Count > 0
            ? await stars.StarredAmongAsync(userId, items.Select(i => i.Id).ToArray(), ct)
            : new HashSet<Guid>();
        return items.Select(i => ItemDto.From(i, starred.Contains(i.Id))).ToArray();
    }

    /// <summary>Một trang kết quả: lấy dư 1 phần tử để biết còn trang sau không.</summary>
    public async Task<ItemPage> PageAsync(IReadOnlyList<DriveItem> fetched, int offset, int pageSize, CancellationToken ct)
    {
        IReadOnlyList<ItemDto> page = await ForAsync(fetched.Take(pageSize).ToArray(), ct);
        return new ItemPage(page, fetched.Count > pageSize ? PageTokens.Encode(offset + pageSize) : null);
    }
}
