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
    private const int MaxHiddenRoots = 5_000;

    public sealed record SearchHit(
        Guid Id, Guid? ParentId, string Kind, string Name, string? MimeType, string Category, long SizeBytes, DateTimeOffset ModifiedAt);

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

        if (term.Length == 0 && category is null && modifiedAfter is null)
        {
            return Results.Ok(new SearchPage([], null));
        }

        Guid me = user.GetRequiredUserId();
        var f = Builders<ItemDocument>.Filter;
        FilterDefinition<ItemDocument> filter = await VisibleAsync(db, me, ct);
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
        FilterDefinition<ItemDocument> filter = await VisibleAsync(db, me, ct) & f.In(x => x.Id, recent.Select(r => r.ItemId));
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

        var f = Builders<ActivityDocument>.Filter;
        // Lọc theo chủ sở hữu ngay trong truy vấn: item của người khác ⇒ danh sách rỗng, không lộ gì.
        FilterDefinition<ActivityDocument> filter = f.Eq(x => x.OwnerId, user.GetRequiredUserId())
            & (f.Eq(x => x.ItemId, itemId) | f.AnyEq(x => x.AncestorIds, itemId));

        List<ActivityDocument> entries = await db.Activity.Find(filter).SortByDescending(x => x.At).Limit(limit).ToListAsync(ct);
        return Results.Ok(entries.Select(a => new ActivityEntry(
            a.Id, a.ItemId, a.Kind, a.Name, a.ActorId, a.Action, a.At, a.PreviousName, a.FromFolder, a.ToFolder, a.VersionNo)));
    }

    /// <summary>
    /// Item của tôi, không phải thư mục gốc, không ở thùng rác: chính nó không bị đánh dấu và không tổ tiên nào là gốc
    /// của một nhóm trong thùng rác.
    /// </summary>
    private static async Task<FilterDefinition<ItemDocument>> VisibleAsync(SearchDb db, Guid me, CancellationToken ct)
    {
        var f = Builders<ItemDocument>.Filter;
        List<Guid> hiddenRoots = await db.Items
            .Find(f.Eq(x => x.OwnerId, me) & f.Eq(x => x.Trashed, true))
            .Project(x => x.Id)
            .Limit(MaxHiddenRoots)
            .ToListAsync(ct);

        FilterDefinition<ItemDocument> visible = f.Eq(x => x.OwnerId, me) & f.Ne(x => x.ParentId, null) & f.Ne(x => x.Trashed, true);
        return hiddenRoots.Count == 0 ? visible : visible & f.AnyNin(x => x.AncestorIds, hiddenRoots);
    }

    private static SearchHit ToHit(ItemDocument d) =>
        new(d.Id, d.ParentId, d.Kind, d.Name, d.MimeType, d.Category, d.SizeBytes, d.UpdatedAt);

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
