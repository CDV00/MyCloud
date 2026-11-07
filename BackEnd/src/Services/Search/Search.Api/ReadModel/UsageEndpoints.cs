using Carter;
using MongoDB.Driver;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Search.Api.ReadModel;

/// <summary>
/// Trang "Bộ nhớ" (Phase 5): dung lượng file của tôi theo loại, phần nằm trong thùng rác, file lớn nhất. Tính trên
/// version hiện tại của mỗi file — dung lượng của version cũ là phần chênh với quota của Storage (client hiển thị
/// "Phiên bản cũ và khác").
/// </summary>
public sealed class UsageEndpoints : ICarterModule
{
    public sealed record CategoryUsage(string Category, long Count, long Bytes);

    public sealed record UsageSummary(IReadOnlyList<CategoryUsage> Categories, CategoryUsage Trash);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Usage").MapGroup("/api/v{version:apiVersion}").HasApiVersion(1).RequireAuthorization();

        api.MapGet("/usage", UsageAsync).WithName("GetUsage");
        api.MapGet("/largest", LargestAsync).WithName("ListLargestFiles");
    }

    private static async Task<IResult> UsageAsync(SearchDb db, ICurrentUser user, CancellationToken ct)
    {
        (FilterDefinition<ItemDocument> active, FilterDefinition<ItemDocument> trashed) = await FiltersAsync(db, user.GetRequiredUserId(), ct);

        var categories = await db.Items.Aggregate()
            .Match(active)
            .Group(x => x.Category, g => new { Category = g.Key, Count = g.LongCount(), Bytes = g.Sum(x => x.SizeBytes) })
            .ToListAsync(ct);
        var trash = await db.Items.Aggregate()
            .Match(trashed)
            .Group(x => 1, g => new { Count = g.LongCount(), Bytes = g.Sum(x => x.SizeBytes) })
            .FirstOrDefaultAsync(ct);

        return Results.Ok(new UsageSummary(
            categories.Select(c => new CategoryUsage(c.Category, c.Count, c.Bytes)).OrderByDescending(c => c.Bytes).ToArray(),
            new CategoryUsage("trash", trash?.Count ?? 0, trash?.Bytes ?? 0)));
    }

    /// <summary>File lớn nhất của tôi (không tính thùng rác).</summary>
    private static async Task<IResult> LargestAsync(SearchDb db, ICurrentUser user, CancellationToken ct, int limit = 50)
    {
        if (limit is < 1 or > 200)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "limit must be between 1 and 200.");
        }

        (FilterDefinition<ItemDocument> active, _) = await FiltersAsync(db, user.GetRequiredUserId(), ct);
        List<ItemDocument> files = await db.Items.Find(active)
            .SortByDescending(x => x.SizeBytes).ThenBy(x => x.Id)
            .Limit(limit)
            .ToListAsync(ct);
        return Results.Ok(files.Select(QueryEndpoints.ToHit).ToArray());
    }

    /// <summary>File của tôi: (đang dùng, nằm trong thùng rác — trực tiếp hoặc vì thư mục chứa nó bị xoá).</summary>
    private static async Task<(FilterDefinition<ItemDocument> Active, FilterDefinition<ItemDocument> Trashed)> FiltersAsync(
        SearchDb db, Guid me, CancellationToken ct)
    {
        var f = Builders<ItemDocument>.Filter;
        FilterDefinition<ItemDocument> mine = f.Eq(x => x.OwnerId, me) & f.Eq(x => x.Kind, "file");
        List<Guid> trashRoots = await db.Items
            .Find(f.Eq(x => x.OwnerId, me) & f.Eq(x => x.Trashed, true))
            .Project(x => x.Id)
            .ToListAsync(ct);
        if (trashRoots.Count == 0)
        {
            return (mine, mine & f.Eq(x => x.Trashed, true)); // vế sau luôn rỗng
        }

        FilterDefinition<ItemDocument> inTrash = f.Eq(x => x.Trashed, true) | f.AnyIn(x => x.AncestorIds, trashRoots);
        return (mine & f.Not(inTrash), mine & inTrash);
    }
}
