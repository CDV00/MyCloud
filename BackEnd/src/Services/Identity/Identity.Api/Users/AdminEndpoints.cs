using Carter;
using Identity.Api.Data;
using Identity.Api.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Authentication;

namespace Identity.Api.Users;

/// <summary>Section <c>Identity</c>.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Email luôn là quản trị viên (gán khi đăng ký / đăng nhập) — tạo quản trị viên đầu tiên. Mỗi phần tử có thể chứa
    /// nhiều email cách nhau bằng dấu phẩy / chấm phẩy (tiện truyền qua một biến môi trường).
    /// </summary>
    public string[] AdminEmails { get; set; } = [];
}

/// <summary>Quản trị viên được chỉ định bằng cấu hình (không cần sửa DB khi triển khai lần đầu).</summary>
public sealed class AdminBootstrap(IOptions<AdminOptions> options)
{
    public bool IsConfiguredAdmin(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && options.Value.AdminEmails
            .SelectMany(a => (a ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(a => string.Equals(a, email.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record AdminUser(
    Guid Id,
    string Email,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsAdmin,
    bool Disabled,
    DateTimeOffset? DisabledAt);

public sealed record AdminUserPage(IReadOnlyList<AdminUser> Items, int Total, int Page, int PageSize);

public sealed record AdminSummary(int Users, int Admins, int Disabled, int ActiveLast30Days);

/// <param name="IsAdmin">null = không đổi.</param>
/// <param name="Disabled">null = không đổi.</param>
public sealed record UpdateAdminUserRequest(bool? IsAdmin, bool? Disabled);

/// <summary>
/// Trang quản trị người dùng (chỉ quản trị viên — policy <see cref="AdminRole.Policy"/>): danh sách / tìm kiếm, khoá /
/// mở khoá (thu hồi mọi phiên đăng nhập), cấp / thu quyền quản trị. Dung lượng do Storage quản lý (<c>/api/storage/v1/admin</c>).
/// </summary>
public sealed class AdminEndpoints : ICarterModule
{
    private const int MaxPageSize = 100;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var admin = app.NewVersionedApi("Admin")
            .MapGroup("/api/v{version:apiVersion}/admin")
            .HasApiVersion(1)
            .RequireAuthorization(AdminRole.Policy);

        admin.MapGet("/summary", SummaryAsync).WithName("AdminSummary");
        admin.MapGet("/users", ListAsync).WithName("AdminListUsers");
        admin.MapPatch("/users/{userId:guid}", UpdateAsync).WithName("AdminUpdateUser");
    }

    private static async Task<IResult> SummaryAsync(AppIdentityDbContext db, TimeProvider time, CancellationToken ct)
    {
        DateTimeOffset since = time.GetUtcNow().AddDays(-30);
        return Results.Ok(new AdminSummary(
            await db.Users.CountAsync(ct),
            await db.Users.CountAsync(u => u.IsAdmin, ct),
            await db.Users.CountAsync(u => u.DisabledAt != null, ct),
            await db.Users.CountAsync(u => u.LastLoginAt >= since, ct)));
    }

    /// <summary><c>?q=</c> tìm theo email / tên (không phân biệt hoa thường); <c>status=all|active|disabled|admin</c>.</summary>
    private static async Task<IResult> ListAsync(
        AppIdentityDbContext db, CancellationToken ct, string? q = null, string? status = null, int page = 1, int pageSize = 50)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize || (q?.Length ?? 0) > 100)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid paging or search parameters.");
        }

        IQueryable<AppUser> users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            string pattern = "%" + EscapeLike(q.Trim()) + "%";
            users = users.Where(u => EF.Functions.ILike(u.Email!, pattern, "\\") || EF.Functions.ILike(u.DisplayName, pattern, "\\"));
        }

        users = status switch
        {
            "active" => users.Where(u => u.DisabledAt == null),
            "disabled" => users.Where(u => u.DisabledAt != null),
            "admin" => users.Where(u => u.IsAdmin),
            _ => users,
        };

        int total = await users.CountAsync(ct);
        List<AppUser> found = await users
            .OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return Results.Ok(new AdminUserPage(found.Select(ToDto).ToArray(), total, page, pageSize));
    }

    private static async Task<IResult> UpdateAsync(
        Guid userId,
        UpdateAdminUserRequest request,
        ICurrentUser currentUser,
        AppIdentityDbContext db,
        RefreshTokenService refreshTokens,
        TimeProvider time,
        ILogger<AdminEndpoints> logger,
        CancellationToken ct)
    {
        // Không tự khoá / tự bỏ quyền của chính mình (tránh hệ thống không còn quản trị viên nào dùng được).
        if (userId == currentUser.GetRequiredUserId())
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "You cannot change your own administrator status or disable your own account.",
                extensions: new Dictionary<string, object?> { ["code"] = "IDENTITY.CANNOT_CHANGE_SELF" });
        }

        AppUser? user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The user does not exist.",
                extensions: new Dictionary<string, object?> { ["code"] = "IDENTITY.USER_NOT_FOUND" });
        }

        if (request.IsAdmin is { } isAdmin)
        {
            user.IsAdmin = isAdmin;
        }

        bool revoke = false;
        if (request.Disabled is { } disabled && disabled != (user.DisabledAt is not null))
        {
            user.DisabledAt = disabled ? time.GetUtcNow() : null;
            revoke = disabled;
        }

        await db.SaveChangesAsync(ct);
        if (revoke)
        {
            // Đăng xuất mọi thiết bị ngay; access token đang dùng hết hạn trong ≤ 15 phút.
            await refreshTokens.RevokeAllForUserAsync(user.Id, RevocationReasons.UserUnavailable, ct);
        }

        AdminLog.UserUpdated(logger, currentUser.GetRequiredUserId(), user.Id, user.IsAdmin, user.DisabledAt is not null);
        return Results.Ok(ToDto(user));
    }

    private static AdminUser ToDto(AppUser u) =>
        new(u.Id, u.Email ?? string.Empty, u.DisplayName, u.CreatedAt, u.LastLoginAt, u.IsAdmin, u.DisabledAt is not null, u.DisabledAt);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}

internal static partial class AdminLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin {AdminId} updated user {UserId}: admin={IsAdmin}, disabled={Disabled}")]
    public static partial void UserUpdated(ILogger logger, Guid adminId, Guid userId, bool isAdmin, bool disabled);
}
