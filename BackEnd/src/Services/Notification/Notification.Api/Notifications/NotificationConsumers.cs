using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyCloud.Contracts.Events.Identity;
using MyCloud.Contracts.Events.Sharing;
using MyCloud.Contracts.Events.Storage;
using Notification.Api.Data;
using Notification.Api.Delivery;

namespace Notification.Api.Notifications;

/// <summary>
/// Event của các service khác ⇒ thông báo. Consumer chạy trong transaction của EF outbox/inbox: lưu thông báo, rồi
/// email / đẩy realtime đi qua outbox nên chỉ xảy ra khi đã lưu, và không lặp khi event được giao lại.
/// </summary>
public sealed class NotificationConsumers(
    NotificationDbContext db,
    IPublishEndpoint publish,
    IOptions<SmtpOptions> smtp,
    TimeProvider timeProvider) :
    IConsumer<UserRegistered>,
    IConsumer<UserProfileChanged>,
    IConsumer<ItemShared>,
    IConsumer<QuotaThresholdReached>,
    IConsumer<ArchiveReady>,
    IConsumer<ArchiveFailed>,
    IConsumer<MyCloud.Contracts.Events.Drive.ItemCommentPosted>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Escape ký tự HTML nhưng giữ nguyên chữ tiếng Việt (WebUtility.HtmlEncode đổi "á" thành "&#225;").
    private static readonly HtmlEncoder HtmlText = HtmlEncoder.Create(UnicodeRanges.All);

    public async Task Consume(ConsumeContext<UserRegistered> context)
    {
        UserRegistered e = context.Message;
        Contact? contact = await db.Contacts.FindAsync([e.UserId], context.CancellationToken);
        if (contact is null)
        {
            db.Contacts.Add(new Contact { UserId = e.UserId, Email = e.Email, DisplayName = e.DisplayName, UpdatedAt = e.OccurredAt });
        }
        else
        {
            contact.Email = e.Email;
            contact.DisplayName = e.DisplayName;
            contact.UpdatedAt = e.OccurredAt;
        }

        string name = Html(e.DisplayName);
        await publish.Publish(new SendEmail(
            e.Email,
            "Chào mừng bạn đến với MyCloud",
            $"<p>Chào {name},</p><p>Tài khoản MyCloud của bạn đã sẵn sàng. Bạn có 15 GB dung lượng miễn phí.</p>"
                + $"<p><a href=\"{smtp.Value.WebBaseUrl}/my-drive\">Mở Drive của tôi</a></p>",
            $"Chào {e.DisplayName},\n\nTài khoản MyCloud của bạn đã sẵn sàng.\n{smtp.Value.WebBaseUrl}/my-drive"), context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<UserProfileChanged> context)
    {
        await db.Contacts.Where(c => c.UserId == context.Message.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DisplayName, context.Message.DisplayName)
                .SetProperty(c => c.UpdatedAt, context.Message.OccurredAt), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<ItemShared> context)
    {
        ItemShared e = context.Message;
        CancellationToken ct = context.CancellationToken;
        Contact? sharer = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == e.SharedBy, ct);
        Contact? grantee = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == e.GranteeId, ct);
        string sharerName = sharer?.DisplayName ?? e.SharedByName ?? "Một người dùng";
        string? granteeEmail = grantee?.Email ?? e.GranteeEmail;

        UserNotification notification = await AddAsync(e.GranteeId, "item_shared", new
        {
            e.ItemId,
            e.ItemName,
            e.ItemKind,
            e.Role,
            e.SharedBy,
            SharedByName = sharerName,
            e.Message,
        }, e.OccurredAt, ct);

        if (e.Notify && !string.IsNullOrEmpty(granteeEmail))
        {
            string url = e.ItemKind == "folder" ? $"{smtp.Value.WebBaseUrl}/my-drive/folders/{e.ItemId}" : $"{smtp.Value.WebBaseUrl}/shared";
            string what = e.ItemKind == "folder" ? "thư mục" : "tệp";
            string role = RoleText(e.Role);
            string note = string.IsNullOrWhiteSpace(e.Message) ? string.Empty : $"<blockquote>{Html(e.Message)}</blockquote>";
            await publish.Publish(new SendEmail(
                granteeEmail,
                $"{sharerName} đã chia sẻ \"{e.ItemName}\" với bạn",
                $"<p><b>{Html(sharerName)}</b> đã chia sẻ {what} <b>{Html(e.ItemName)}</b> với bạn ({role}).</p>"
                    + note + $"<p><a href=\"{url}\">Mở trong MyCloud</a></p>",
                $"{sharerName} đã chia sẻ {what} \"{e.ItemName}\" với bạn ({role}).\n{e.Message}\n{url}"), ct);
        }

        await db.SaveChangesAsync(ct);
        await publish.Publish(new PushNotification(e.GranteeId, notification.Id), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task Consume(ConsumeContext<QuotaThresholdReached> context)
    {
        QuotaThresholdReached e = context.Message;
        UserNotification notification = await AddAsync(e.UserId, "quota", new { e.Percent, e.UsedBytes, e.LimitBytes }, e.OccurredAt, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
        await publish.Publish(new PushNotification(e.UserId, notification.Id), context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    /// <summary>Tệp zip đã sẵn sàng (ADR-015) — chỉ thông báo trong app (realtime), không gửi email.</summary>
    public Task Consume(ConsumeContext<ArchiveReady> context)
    {
        ArchiveReady e = context.Message;
        return NotifyAsync(e.UserId, "archive_ready", new { e.ArchiveId, e.Name, e.SizeBytes, e.FileCount, e.SkippedCount, e.ExpiresAt }, e.OccurredAt, context);
    }

    public Task Consume(ConsumeContext<ArchiveFailed> context)
    {
        ArchiveFailed e = context.Message;
        return NotifyAsync(e.UserId, "archive_failed", new { e.ArchiveId, e.Name, e.Reason }, e.OccurredAt, context);
    }

    /// <summary>
    /// Tin nhắn mới trong trao đổi về tệp (ADR-017): thông báo trong app cho từng người nhận — "nhắc đến bạn" nếu được
    /// nhắc tên, "góp ý phiên bản N" nếu gắn với phiên bản, còn lại "trả lời / bình luận".
    /// </summary>
    public async Task Consume(ConsumeContext<MyCloud.Contracts.Events.Drive.ItemCommentPosted> context)
    {
        var e = context.Message;
        var mentioned = e.Mentions.ToHashSet();
        var created = new List<(Guid UserId, Guid NotificationId)>();
        foreach (Guid recipient in e.Recipients.Distinct().Where(r => r != e.AuthorId).Take(100))
        {
            UserNotification notification = await AddAsync(recipient, "comment", new
            {
                e.ItemId,
                e.ItemName,
                e.CommentId,
                e.VersionId,
                e.VersionNo,
                IsReply = e.ParentId is not null,
                Mentioned = mentioned.Contains(recipient),
                e.AuthorId,
                e.AuthorName,
                e.Excerpt,
            }, e.OccurredAt, context.CancellationToken);
            created.Add((recipient, notification.Id));
        }

        await db.SaveChangesAsync(context.CancellationToken);
        foreach ((Guid userId, Guid id) in created)
        {
            await publish.Publish(new PushNotification(userId, id), context.CancellationToken);
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }

    private async Task NotifyAsync(Guid userId, string type, object payload, DateTimeOffset at, ConsumeContext context)
    {
        UserNotification notification = await AddAsync(userId, type, payload, at, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
        await publish.Publish(new PushNotification(userId, notification.Id), context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    private Task<UserNotification> AddAsync(Guid userId, string type, object payload, DateTimeOffset at, CancellationToken ct)
    {
        var notification = new UserNotification
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = type,
            Payload = JsonSerializer.Serialize(payload, Json),
            CreatedAt = at == default ? timeProvider.GetUtcNow() : at,
        };
        db.Notifications.Add(notification);
        return Task.FromResult(notification);
    }

    private static string Html(string text) => HtmlText.Encode(text);

    private static string RoleText(string role) => role switch
    {
        "editor" => "có thể chỉnh sửa",
        "commenter" => "có thể nhận xét",
        _ => "có thể xem",
    };
}
