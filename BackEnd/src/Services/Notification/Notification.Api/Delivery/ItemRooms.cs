using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.Contracts.Events.Drive;

namespace Notification.Api.Delivery;

/// <summary>Section <c>Collaboration</c>: secret dùng chung với Drive để kiểm tra vé vào phòng.</summary>
public sealed class CollaborationOptions
{
    public const string SectionName = "Collaboration";

    public string Secret { get; set; } = string.Empty;
}

/// <summary>Có hoạt động mới trong phòng của tệp ⇒ client đang mở lịch sử / trao đổi tải lại phần tương ứng.</summary>
public sealed record ItemActivity(Guid ItemId, string Kind, Guid? ActorId);

/// <summary>Ai đang xem / đang sửa tệp ("viewing" | "editing" | "left").</summary>
public sealed record PresenceUpdate(Guid ItemId, Guid UserId, string Name, string Mode, DateTimeOffset At);

/// <summary>
/// "Phòng" realtime của từng tệp trên hub thông báo (ADR-017). Vào phòng cần vé Drive cấp sau khi kiểm tra quyền xem
/// (<see cref="LiveRoomToken"/>); hub chỉ kiểm tra chữ ký nên không phải gọi Drive. Hiện diện (ai đang xem / sửa) là tin
/// nhịp tim client gửi mỗi ~20 giây, không lưu trên server — tab đóng đột ngột thì client khác tự xoá sau ~1 phút.
/// </summary>
public static class ItemRooms
{
    public const string ActivityMethod = "item-activity";
    public const string PresenceMethod = "presence";
    private const string RoomsKey = "mycloud.rooms";

    public static string Group(Guid itemId) => $"item:{itemId:N}";

    public static HashSet<Guid> Rooms(HubCallerContext context)
    {
        if (context.Items.TryGetValue(RoomsKey, out object? value) && value is HashSet<Guid> rooms)
        {
            return rooms;
        }

        var created = new HashSet<Guid>();
        context.Items[RoomsKey] = created;
        return created;
    }
}

/// <summary>Phương thức client gọi trên hub (phần phòng của tệp).</summary>
public sealed partial class NotificationHub
{
    /// <returns>false nếu vé không hợp lệ / hết hạn.</returns>
    public async Task<bool> Watch(string token)
    {
        if (!Guid.TryParse(Context.User?.FindFirstValue("sub"), out Guid userId))
        {
            return false;
        }

        string secret = Context.GetHttpContext()!.RequestServices.GetRequiredService<IOptions<CollaborationOptions>>().Value.Secret;
        TimeProvider time = Context.GetHttpContext()!.RequestServices.GetRequiredService<TimeProvider>();
        if (string.IsNullOrEmpty(secret) || LiveRoomToken.Verify(secret, token, userId, time.GetUtcNow()) is not { } itemId)
        {
            return false;
        }

        HashSet<Guid> rooms = ItemRooms.Rooms(Context);
        lock (rooms)
        {
            if (rooms.Count >= 20 && !rooms.Contains(itemId))
            {
                return false; // một kết nối không theo dõi quá nhiều tệp cùng lúc
            }

            rooms.Add(itemId);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ItemRooms.Group(itemId));
        return true;
    }

    public async Task Unwatch(string itemId)
    {
        if (!Guid.TryParse(itemId, out Guid id))
        {
            return;
        }

        HashSet<Guid> rooms = ItemRooms.Rooms(Context);
        bool removed;
        lock (rooms)
        {
            removed = rooms.Remove(id);
        }

        if (removed)
        {
            await AnnounceAsync(id, "left");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ItemRooms.Group(id));
        }
    }

    /// <summary>Nhịp tim hiện diện: "viewing" hoặc "editing". Chỉ trong phòng đã vào.</summary>
    public Task Presence(string itemId, string mode)
    {
        if (!Guid.TryParse(itemId, out Guid id) || mode is not ("viewing" or "editing"))
        {
            return Task.CompletedTask;
        }

        HashSet<Guid> rooms = ItemRooms.Rooms(Context);
        lock (rooms)
        {
            if (!rooms.Contains(id))
            {
                return Task.CompletedTask;
            }
        }

        return AnnounceAsync(id, mode);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Guid[] rooms;
        HashSet<Guid> set = ItemRooms.Rooms(Context);
        lock (set)
        {
            rooms = set.ToArray();
        }

        foreach (Guid id in rooms)
        {
            await AnnounceAsync(id, "left");
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task AnnounceAsync(Guid itemId, string mode)
    {
        if (!Guid.TryParse(Context.User?.FindFirstValue("sub"), out Guid userId))
        {
            return Task.CompletedTask;
        }

        string name = Context.User?.FindFirstValue("name") ?? "Người dùng";
        return Clients.OthersInGroup(ItemRooms.Group(itemId)).SendAsync(
            ItemRooms.PresenceMethod, new PresenceUpdate(itemId, userId, name, mode, DateTimeOffset.UtcNow));
    }
}

/// <summary>Đẩy hoạt động mới (tin nhắn, phiên bản, sửa ghi chú…) tới người đang mở tệp.</summary>
public sealed class ItemRoomBroadcaster(IHubContext<NotificationHub> hub)
    : IConsumer<ItemCommentPosted>, IConsumer<ItemHistoryChanged>, IConsumer<FileVersionAdded>
{
    public Task Consume(ConsumeContext<ItemCommentPosted> context) =>
        Send(context.Message.ItemId, "comment", context.Message.AuthorId, context.CancellationToken);

    public Task Consume(ConsumeContext<ItemHistoryChanged> context) =>
        Send(context.Message.ItemId, context.Message.Kind, null, context.CancellationToken);

    public Task Consume(ConsumeContext<FileVersionAdded> context) =>
        Send(context.Message.ItemId, "version", context.Message.By, context.CancellationToken);

    private Task Send(Guid itemId, string kind, Guid? actor, CancellationToken ct) =>
        hub.Clients.Group(ItemRooms.Group(itemId)).SendAsync(ItemRooms.ActivityMethod, new ItemActivity(itemId, kind, actor), ct);
}
