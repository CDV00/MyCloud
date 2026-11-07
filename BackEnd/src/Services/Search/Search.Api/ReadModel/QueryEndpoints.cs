using System.Text.RegularExpressions;
using Carter;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Search.Api.ReadModel;

/// <summary>
/// Tìm theo tên trong Drive của chính mình. Danh sách thư mục đọc thẳng từ Drive (nhất quán ngay — ADR-003);
/// Search chấp nhận trễ vài giây. Phase 3: full-text, lọc theo loại, "Gần đây".
/// </summary>
public sealed class QueryEndpoints : ICarterModule
{
    private const int MaxResults = 50;

    public sealed record SearchHit(Guid Id, Guid? ParentId, string Kind, string Name, string? MimeType, long SizeBytes, DateTimeOffset UpdatedAt);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.NewVersionedApi("Search")
            .MapGroup("/api/v{version:apiVersion}")
            .HasApiVersion(1)
            .RequireAuthorization()
            .MapGet("/search", SearchAsync)
            .WithName("SearchItems");
    }

    private static async Task<IResult> SearchAsync(string? q, SearchDb db, ICurrentUser user, CancellationToken ct)
    {
        string term = (q ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length is 0 or > 100)
        {
            return Results.Ok(Array.Empty<SearchHit>());
        }

        var f = Builders<ItemDocument>.Filter;
        FilterDefinition<ItemDocument> filter =
            f.Eq(x => x.OwnerId, user.GetRequiredUserId())
            & f.Regex(x => x.NameLower, new BsonRegularExpression(Regex.Escape(term)));

        List<ItemDocument> items = await db.Items.Find(filter).SortBy(x => x.NameLower).Limit(MaxResults).ToListAsync(ct);
        return Results.Ok(items.Select(d => new SearchHit(d.Id, d.ParentId, d.Kind, d.Name, d.MimeType, d.SizeBytes, d.UpdatedAt)));
    }
}
