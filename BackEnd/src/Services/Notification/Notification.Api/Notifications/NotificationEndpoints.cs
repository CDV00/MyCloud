using System.Text.Json;
using Carter;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Application.Abstractions;
using Notification.Api.Data;

namespace Notification.Api.Notifications;

public sealed record NotificationDto(Guid Id, string Type, JsonElement Payload, DateTimeOffset CreatedAt, bool Read);

public sealed record NotificationList(IReadOnlyList<NotificationDto> Items, int UnreadCount, DateTimeOffset? NextBefore);

public sealed class NotificationQueries(NotificationDbContext db)
{
    public async Task<NotificationDto?> FindAsync(Guid userId, Guid id, CancellationToken ct)
    {
        UserNotification? n = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        return n is null ? null : ToDto(n);
    }

    public static NotificationDto ToDto(UserNotification n) =>
        new(n.Id, n.Type, JsonDocument.Parse(n.Payload).RootElement.Clone(), n.CreatedAt, n.ReadAt is not null);
}

/// <summary>Thông báo của chính mình (docs/03 §7). Phân trang theo thời gian (<c>before</c>), mới nhất trước.</summary>
public sealed class NotificationEndpoints : ICarterModule
{
    private const int PageSize = 30;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Notifications").MapGroup("/api/v{version:apiVersion}/notifications").HasApiVersion(1).RequireAuthorization();

        api.MapGet("/", ListAsync).WithName("ListNotifications");
        api.MapPost("/{id:guid}/read", MarkReadAsync).WithName("MarkNotificationRead");
        api.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead");
    }

    private static async Task<IResult> ListAsync(
        NotificationDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct, bool unreadOnly = false, DateTimeOffset? before = null)
    {
        Guid me = user.GetRequiredUserId();
        IQueryable<UserNotification> query = db.Notifications.AsNoTracking().Where(n => n.UserId == me);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        if (before is { } b)
        {
            query = query.Where(n => n.CreatedAt < b);
        }

        List<UserNotification> page = await query.OrderByDescending(n => n.CreatedAt).Take(PageSize + 1).ToListAsync(ct);
        int unread = await db.Notifications.CountAsync(n => n.UserId == me && n.ReadAt == null, ct);
        return Results.Ok(new NotificationList(
            page.Take(PageSize).Select(NotificationQueries.ToDto).ToArray(),
            unread,
            page.Count > PageSize ? page[PageSize - 1].CreatedAt : null));
    }

    private static async Task<IResult> MarkReadAsync(Guid id, NotificationDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        Guid me = user.GetRequiredUserId();
        int updated = await db.Notifications.Where(n => n.Id == id && n.UserId == me && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, time.GetUtcNow()), ct);
        return updated > 0 || await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == me, ct) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> MarkAllReadAsync(NotificationDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        Guid me = user.GetRequiredUserId();
        await db.Notifications.Where(n => n.UserId == me && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, time.GetUtcNow()), ct);
        return Results.NoContent();
    }
}
