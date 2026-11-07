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

internal sealed class GetItemQueryHandler(ItemAccess access) : IQueryHandler<GetItemQuery, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(GetItemQuery request, CancellationToken cancellationToken) =>
        ItemDto.From(await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken));
}

/// <summary>Breadcrumb: từ gốc tới cha trực tiếp — đọc id tổ tiên từ materialized path, nạp bằng 1 truy vấn.</summary>
public sealed record GetAncestorsQuery(Guid ItemId) : IQuery<IReadOnlyList<ItemDto>>;

internal sealed class GetAncestorsQueryHandler(ItemAccess access, IDriveItemRepository items)
    : IQueryHandler<GetAncestorsQuery, IReadOnlyList<ItemDto>>
{
    public async Task<Result<IReadOnlyList<ItemDto>>> Handle(GetAncestorsQuery request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        IReadOnlyList<DriveItem> ancestors = await items.FindManyAsync(item.AncestorIds.ToArray(), cancellationToken);
        return ancestors.OrderBy(a => a.Depth).Select(ItemDto.From).ToArray();
    }
}

/// <summary>
/// Nội dung thư mục — đọc thẳng PostgreSQL nên thấy ngay thứ vừa tạo (ADR-003). Thư mục luôn đứng trước file.
/// <c>PageToken</c> là chuỗi mờ (opaque) do server cấp; hiện mã hoá vị trí, có thể đổi sang keyset mà không đổi API.
/// </summary>
public sealed record ListChildrenQuery(Guid FolderId, ChildOrder OrderBy, bool Descending, int PageSize, string? PageToken)
    : IQuery<ItemPage>
{
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 100;
}

internal sealed class ListChildrenQueryValidator : AbstractValidator<ListChildrenQuery>
{
    public ListChildrenQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListChildrenQuery.MaxPageSize);
        RuleFor(x => x.PageToken).Must(t => t is null || PageTokens.TryDecode(t, out _)).WithMessage("Invalid page token.");
    }
}

internal sealed class ListChildrenQueryHandler(ItemAccess access, IDriveItemRepository items) : IQueryHandler<ListChildrenQuery, ItemPage>
{
    public async Task<Result<ItemPage>> Handle(ListChildrenQuery request, CancellationToken cancellationToken)
    {
        DriveItem folder = await access.GetFolderAsync(request.FolderId, ItemPermission.View, cancellationToken);
        int offset = request.PageToken is null ? 0 : PageTokens.Decode(request.PageToken);

        // Lấy dư 1 để biết còn trang sau không.
        IReadOnlyList<DriveItem> page = await items.ListChildrenAsync(
            folder.Id, request.OrderBy, request.Descending, offset, request.PageSize + 1, cancellationToken);

        bool hasMore = page.Count > request.PageSize;
        return new ItemPage(
            page.Take(request.PageSize).Select(ItemDto.From).ToArray(),
            hasMore ? PageTokens.Encode(offset + request.PageSize) : null);
    }
}

internal static class PageTokens
{
    private const string Prefix = "o:";

    public static string Encode(int offset) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(Prefix + offset.ToString(CultureInfo.InvariantCulture)));

    public static int Decode(string token) => TryDecode(token, out int offset) ? offset : 0;

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
