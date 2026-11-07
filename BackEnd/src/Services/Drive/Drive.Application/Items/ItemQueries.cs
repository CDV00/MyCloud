using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Items;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Items;

public sealed record GetItemQuery(Guid ItemId) : IQuery<ItemDto>;

internal sealed class GetItemQueryHandler(ItemAccess access, ItemDtos dtos) : IQueryHandler<GetItemQuery, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(GetItemQuery request, CancellationToken cancellationToken) =>
        await dtos.ForAsync(await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken), cancellationToken);
}

/// <summary>Breadcrumb: từ gốc tới cha trực tiếp — đọc id tổ tiên từ materialized path, nạp bằng 1 truy vấn.</summary>
public sealed record GetAncestorsQuery(Guid ItemId) : IQuery<IReadOnlyList<ItemDto>>;

internal sealed class GetAncestorsQueryHandler(ItemAccess access, IDriveItemRepository items, ItemDtos dtos)
    : IQueryHandler<GetAncestorsQuery, IReadOnlyList<ItemDto>>
{
    public async Task<Result<IReadOnlyList<ItemDto>>> Handle(GetAncestorsQuery request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        DriveItem[] ancestors = (await items.FindManyAsync(item.AncestorIds.ToArray(), cancellationToken)).OrderBy(a => a.Depth).ToArray();

        // Người được chia sẻ chỉ thấy phần cây từ thư mục được chia sẻ trở xuống (không thấy "Drive của tôi" của chủ).
        IReadOnlyList<AccessRole> roles = await access.RolesAsync(ancestors, cancellationToken);
        int firstVisible = Array.FindIndex(roles.ToArray(), r => r >= AccessRole.Viewer);
        DriveItem[] visible = firstVisible < 0 ? [] : ancestors[firstVisible..];
        return (await dtos.ForAsync(visible, cancellationToken)).ToArray();
    }
}

/// <summary>Truy vấn có phân trang. <c>PageToken</c> là chuỗi mờ do server cấp.</summary>
public interface IPagedQuery
{
    int PageSize { get; }

    string? PageToken { get; }
}

public static class Paging
{
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 100;
}

internal abstract class PagedQueryValidator<T> : AbstractValidator<T>
    where T : IPagedQuery
{
    protected PagedQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.PageToken).Must(t => t is null || PageTokens.TryDecode(t, out _)).WithMessage("Invalid page token.");
    }
}

/// <summary>
/// Nội dung thư mục — đọc thẳng PostgreSQL nên thấy ngay thứ vừa tạo (ADR-003). Thư mục luôn đứng trước file.
/// Thư mục đang ở thùng rác vẫn mở được (chỉ xem) và hiện các item bị xoá cùng nó.
/// </summary>
public sealed record ListChildrenQuery(Guid FolderId, ChildOrder OrderBy, bool Descending, int PageSize, string? PageToken)
    : IQuery<ItemPage>, IPagedQuery;

internal sealed class ListChildrenQueryValidator : PagedQueryValidator<ListChildrenQuery>;

internal sealed class ListChildrenQueryHandler(ItemAccess access, IDriveItemRepository items, ItemDtos dtos)
    : IQueryHandler<ListChildrenQuery, ItemPage>
{
    public async Task<Result<ItemPage>> Handle(ListChildrenQuery request, CancellationToken cancellationToken)
    {
        DriveItem folder = await access.GetAsync(request.FolderId, ItemPermission.View, cancellationToken);
        if (!folder.IsFolder)
        {
            throw new DriveException.NotAFolder(folder.Id);
        }

        int offset = PageTokens.DecodeOrZero(request.PageToken);
        IReadOnlyList<DriveItem> page = await items.ListChildrenAsync(
            folder.Id, folder.TrashRootId, request.OrderBy, request.Descending, offset, request.PageSize + 1, cancellationToken);
        return await dtos.PageAsync(page, offset, request.PageSize, cancellationToken);
    }
}

/// <summary>Thùng rác: item người dùng đã xoá, mới xoá trước (item nằm trong thư mục đã xoá không hiện riêng).</summary>
public sealed record ListTrashQuery(int PageSize, string? PageToken) : IQuery<ItemPage>, IPagedQuery;

internal sealed class ListTrashQueryValidator : PagedQueryValidator<ListTrashQuery>;

internal sealed class ListTrashQueryHandler(ItemAccess access, IDriveItemRepository items, ItemDtos dtos)
    : IQueryHandler<ListTrashQuery, ItemPage>
{
    public async Task<Result<ItemPage>> Handle(ListTrashQuery request, CancellationToken cancellationToken)
    {
        int offset = PageTokens.DecodeOrZero(request.PageToken);
        IReadOnlyList<DriveItem> page = await items.ListTrashAsync(access.UserId, offset, request.PageSize + 1, cancellationToken);
        return await dtos.PageAsync(page, offset, request.PageSize, cancellationToken);
    }
}

/// <summary>"Có gắn dấu sao": item chưa bị xoá, gắn sao gần đây trước.</summary>
public sealed record ListStarredQuery(int PageSize, string? PageToken) : IQuery<ItemPage>, IPagedQuery;

internal sealed class ListStarredQueryValidator : PagedQueryValidator<ListStarredQuery>;

internal sealed class ListStarredQueryHandler(ItemAccess access, IStarRepository stars, ItemDtos dtos)
    : IQueryHandler<ListStarredQuery, ItemPage>
{
    public async Task<Result<ItemPage>> Handle(ListStarredQuery request, CancellationToken cancellationToken)
    {
        int offset = PageTokens.DecodeOrZero(request.PageToken);
        IReadOnlyList<DriveItem> page = await stars.ListStarredItemsAsync(access.UserId, offset, request.PageSize + 1, cancellationToken);
        bool hasMore = page.Count > request.PageSize;

        // Item của người khác mà quyền đã bị thu hồi: ẩn (sao vẫn còn, quay lại nếu được chia sẻ lại).
        DriveItem[] current = page.Take(request.PageSize).ToArray();
        IReadOnlyList<AccessRole> roles = await access.RolesAsync(current, cancellationToken);
        DriveItem[] visible = current.Where((_, i) => roles[i] >= AccessRole.Viewer).ToArray();
        return new ItemPage(await dtos.ForAsync(visible, cancellationToken), hasMore ? PageTokens.Encode(offset + request.PageSize) : null);
    }
}

/// <summary>Token trang hiện mã hoá vị trí; có thể đổi sang keyset mà không đổi API.</summary>
internal static class PageTokens
{
    private const string Prefix = "o:";

    public static string Encode(int offset) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(Prefix + offset.ToString(CultureInfo.InvariantCulture)));

    public static int DecodeOrZero(string? token) => token is not null && TryDecode(token, out int offset) ? offset : 0;

    public static bool TryDecode(string token, out int offset)
    {
        offset = 0;
        try
        {
            string raw = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(token));
            return raw.StartsWith(Prefix, StringComparison.Ordinal)
                && int.TryParse(raw.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                && offset is >= 0 and <= 1_000_000;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
