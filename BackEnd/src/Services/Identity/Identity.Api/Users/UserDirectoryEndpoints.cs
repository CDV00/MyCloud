using Carter;
using Identity.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Users;

public sealed record UserSummary(Guid Id, string DisplayName, string Email, string? AvatarUrl);

/// <summary>
/// Tên hiển thị cho danh sách id (docs/03 §2 <c>users:batchGet</c>): "Chủ sở hữu", "Ai đã sửa", người được chia sẻ.
/// Chỉ trả về theo id đã biết (không liệt kê / tìm theo tên) để không thành công cụ dò tài khoản.
/// </summary>
public sealed class UserDirectoryEndpoints : ICarterModule
{
    private const int MaxIds = 100;

    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.NewVersionedApi("UserDirectory")
            .MapGroup("/api/v{version:apiVersion}/users")
            .HasApiVersion(1)
            .RequireAuthorization()
            .MapGet("/", BatchGetAsync)
            .WithName("BatchGetUsers");

    private static async Task<IResult> BatchGetAsync(string? ids, AppIdentityDbContext db, CancellationToken ct)
    {
        Guid[] wanted = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => Guid.TryParse(id, out Guid g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToArray();
        if (wanted.Length > MaxIds)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: $"At most {MaxIds} ids per request.");
        }

        List<UserSummary> users = wanted.Length == 0
            ? []
            : await db.Users.AsNoTracking()
                .Where(u => wanted.Contains(u.Id))
                .Select(u => new UserSummary(u.Id, u.DisplayName, u.Email ?? string.Empty, u.AvatarUrl))
                .ToListAsync(ct);
        return Results.Ok(users);
    }
}
