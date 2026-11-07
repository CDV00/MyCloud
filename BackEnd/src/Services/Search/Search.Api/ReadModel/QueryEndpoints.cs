using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Carter;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Search.Api.ReadModel;

/// <summary>
/// Các view chấp nhận trễ vài giây (docs/03 §6): tìm kiếm, "Gần đây", hoạt động. Danh sách thư mục vẫn đọc thẳng từ
/// Drive (nhất quán ngay — ADR-003). Phase 4 mở rộng điều kiện quyền sang item được chia sẻ (<c>access_grants</c>).
/// </summary>
public sealed class QueryEndpoints : ICarterModule
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public sealed record SearchHit(
        Guid Id, Guid? ParentId, string Kind, string Name, string? MimeType, string Category, long SizeBytes, DateTimeOffset ModifiedAt,
        Guid OwnerId);

    /// <summary>Item được chia sẻ trực tiếp cho tôi.</summary>
    public sealed record SharedHit(SearchHit Item, string Role, Guid SharedBy, DateTimeOffset SharedAt);

    public sealed record SearchPage(IReadOnlyList<SearchHit> Items, string? NextPageToken);

    public sealed record RecentHit(SearchHit Item, DateTimeOffset LastAccessAt, string Action);

    public sealed record ActivityEntry(
        Guid Id,
        Guid ItemId,
        string Kind,
        string Name,
        Guid ActorId,
        string Action,
        DateTimeOffset At,
        string? PreviousName,
        string? FromFolder,
        string? ToFolder,
        int? VersionNo);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Search").MapGroup("/api/v{version:apiVersion}").HasApiVersion(1).RequireAuthorization();

        api.MapGet("/search", SearchAsync).WithName("SearchItems");
        api.MapGet("/recent", RecentAsync).WithName("ListRecent");
        api.MapGet("/items/{itemId:guid}/activity", ActivityAsync).WithName("ListActivity");
        api.MapGet("/shared-with-me", SharedWithMeAsync).WithName("ListSharedWithMe");
    }

    /// <summary>
    /// <c>GET /search?q=&amp;type=&amp;modifiedAfter=&amp;pageToken=</c>. Tên khớp một phần, không phân biệt hoa thường
    /// và không dấu ("bao cao" thấy "Báo cáo"); mới sửa trước. Item trong thùng rác (kể cả nằm trong thư mục đã xoá) bị ẩn.
    /// </summary>
    private static async Task<IResult> SearchAsync(
        SearchDb db,
        ICurrentUser user,
        CancellationToken ct,
        string? q = null,
        string? type = null,
        string? owner = null,
        DateTimeOffset? modifiedAfter = null,
        int pageSize = DefaultPageSize,
        string? pageToken = null)
    {
        string term = TextFolding.Fold((q ?? string.Empty).Trim());
        string? category = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
        if (term.Length > 100 || (category is not null && !Categories.All.Contains(category)) || pageSize is < 1 or > MaxPageSize
            || !TryDecodeOffset(pageToken, out int offset))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid search parameters.");
        }

        if (owner is not (null or "" or "me" or "others"))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "owner must be 'me' or 'others'.");
        }

        if (term.Length == 0 && category is null && modifiedAfter is null && string.IsNullOrEmpty(owner))
        {
            return Results.Ok(new SearchPage([], null));
        }

        Guid me = user.GetRequiredUserId();
        var f = Builders<ItemDocument>.Filter;
        FilterDefinition<ItemDocument> filter = (await Visibility.ForAsync(db, me, ct)).Filter;
        if (owner == "me")
        {
            filter &= f.Eq(x => x.OwnerId, me);
        }
        else if (owner == "others")
        {
            filter &= f.Ne(x => x.OwnerId, me);
        }
        if (term.Length > 0)
        {
            filter &= f.Regex(x => x.NameFolded, new BsonRegularExpression(Regex.Escape(term)));
        }

        if (category is not null)
        {
            filter &= f.Eq(x => x.Category, category);
        }

        if (modifiedAfter is { } after)
        {
            filter &= f.Gte(x => x.UpdatedAt, after);
        }

        List<ItemDocument> found = await db.Items.Find(filter)
            .SortByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Skip(offset).Limit(pageSize + 1)
            .ToListAsync(ct);

        return Results.Ok(new SearchPage(
            found.Take(pageSize).Select(ToHit).ToArray(),
            found.Count > pageSize ? EncodeOffset(offset + pageSize) : null));
    }

    /// <summary>"Gần đây": file người dùng mở / tải / tải lên gần nhất (đã ẩn item trong thùng rác hoặc đã xoá).</summary>
    private static async Task<IResult> RecentAsync(SearchDb db, ICurrentUser user, CancellationToken ct, int pageSize = DefaultPageSize)
    {
        if (pageSize is < 1 or > MaxPageSize)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid page size.");
        }

        Guid me = user.GetRequiredUserId();
        List<RecentDocument> recent = await db.Recent.Find(Builders<RecentDocument>.Filter.Eq(x => x.UserId, me))
            .SortByDescending(x => x.LastAccessAt)
            .Limit(pageSize * 2) // dư ra cho các item đã bị ẩn
            .ToListAsync(ct);
        if (recent.Count == 0)
        {
            return Results.Ok(Array.Empty<RecentHit>());
        }

        var f = Builders<ItemDocument>.Filter;
        FilterDefinition<ItemDocument> filter = (await Visibility.ForAsync(db, me, ct)).Filter & f.In(x => x.Id, recent.Select(r => r.ItemId));
        Dictionary<Guid, ItemDocument> items = (await db.Items.Find(filter).ToListAsync(ct)).ToDictionary(d => d.Id);

        return Results.Ok(recent
            .Where(r => items.ContainsKey(r.ItemId))
            .Take(pageSize)
            .Select(r => new RecentHit(ToHit(items[r.ItemId]), r.LastAccessAt, r.Action))
            .ToArray());
    }

    /// <summary>Hoạt động của một item; với thư mục gồm cả hoạt động của các item bên trong. Mới nhất trước.</summary>
    private static async Task<IResult> ActivityAsync(Guid itemId, SearchDb db, ICurrentUser user, CancellationToken ct, int limit = DefaultPageSize)
    {
        if (limit is < 1 or > MaxPageSize)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid limit.");
        }

        // Chỉ khi item thấy được (của tôi / được chia sẻ): không thấy ⇒ danh sách rỗng, không lộ gì.
        Guid me = user.GetRequiredUserId();
        FilterDefinition<ItemDocument> visible = (await Visibility.ForAsync(db, me, ct)).Filter;
        ItemDocument? item = await db.Items.Find(visible & Builders<ItemDocument>.Filter.Eq(x => x.Id, itemId)).FirstOrDefaultAsync(ct);
        if (item is null)
        {
            return Results.Ok(Array.Empty<ActivityEntry>());
        }

        var f = Builders<ActivityDocument>.Filter;
        FilterDefinition<ActivityDocument> filter = f.Eq(x => x.OwnerId, item.OwnerId)
            & (f.Eq(x => x.ItemId, itemId) | f.AnyEq(x => x.AncestorIds, itemId));

        List<ActivityDocument> entries = await db.Activity.Find(filter).SortByDescending(x => x.At).Limit(limit).ToListAsync(ct);
        return Results.Ok(entries.Select(a => new ActivityEntry(
            a.Id, a.ItemId, a.Kind, a.Name, a.ActorId, a.Action, a.At, a.PreviousName, a.FromFolder, a.ToFolder, a.VersionNo)));
    }

    private static SearchHit ToHit(ItemDocument d) =>
        new(d.Id, d.ParentId, d.Kind, d.Name, d.MimeType, d.Category, d.SizeBytes, d.UpdatedAt, d.OwnerId);

    /// <summary>"Được chia sẻ với tôi": item được chia sẻ trực tiếp (không liệt kê từng item con), mới chia sẻ trước.</summary>
    private static async Task<IResult> SharedWithMeAsync(SearchDb db, ICurrentUser user, CancellationToken ct)
    {
        Guid me = user.GetRequiredUserId();
        (FilterDefinition<ItemDocument> visible, IReadOnlyList<AccessGrantDocument> grants) = await Visibility.ForAsync(db, me, ct);
        if (grants.Count == 0)
        {
            return Results.Ok(Array.Empty<SharedHit>());
        }

        var f = Builders<ItemDocument>.Filter;
        Dictionary<Guid, ItemDocument> items = (await db.Items
                .Find(visible & f.In(x => x.Id, grants.Select(g => g.ItemId)))
                .ToListAsync(ct))
            .ToDictionary(d => d.Id);
        return Results.Ok(grants
            .Where(g => items.ContainsKey(g.ItemId))
            .OrderByDescending(g => g.SharedAt)
            .DistinctBy(g => g.ItemId)
            .Select(g => new SharedHit(ToHit(items[g.ItemId]), g.Role, g.SharedBy, g.SharedAt))
            .ToArray());
    }

    private static string EncodeOffset(int offset) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes("o:" + offset.ToString(CultureInfo.InvariantCulture)));

    private static bool TryDecodeOffset(string? token, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        try
        {
            string raw = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(token));
            return raw.StartsWith("o:", StringComparison.Ordinal)
                && int.TryParse(raw.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                && offset <= 10_000;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
