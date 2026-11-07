using System.Security.Claims;
using MailKit.Net.Smtp;
using MailKit.Security;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MimeKit;
using Notification.Api.Notifications;

namespace Notification.Api.Delivery;

// Bản tin nội bộ của Notification (không thuộc hợp đồng chung): đi qua outbox của chính service ⇒ chỉ gửi email /
// đẩy realtime khi thông báo đã được lưu, và được retry độc lập (SMTP chập chờn không làm mất thông báo).

/// <summary>Đẩy thông báo vừa lưu tới các tab đang mở của người dùng.</summary>
public sealed record PushNotification(Guid UserId, Guid NotificationId);

public sealed record SendEmail(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Hub <c>/api/hubs/notifications</c> (qua Gateway: <c>/api/notification/hubs/notifications</c>): đẩy thông báo xuống, và
/// "phòng" realtime của từng tệp — xem <c>ItemRooms.cs</c>.
/// </summary>
[Authorize]
public sealed partial class NotificationHub : Hub
{
    public const string Path = "/api/hubs/notifications";
    public const string NotificationMethod = "notification";
}

/// <summary>SignalR nhận diện người dùng bằng claim <c>sub</c> (token giữ nguyên tên claim, không map sang NameIdentifier).</summary>
public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirstValue("sub");
}

public sealed class PushNotificationConsumer(IHubContext<NotificationHub> hub, NotificationQueries queries) : IConsumer<PushNotification>
{
    public async Task Consume(ConsumeContext<PushNotification> context)
    {
        NotificationDto? dto = await queries.FindAsync(context.Message.UserId, context.Message.NotificationId, context.CancellationToken);
        if (dto is not null)
        {
            await hub.Clients.User(context.Message.UserId.ToString()).SendAsync(NotificationHub.NotificationMethod, dto, context.CancellationToken);
        }
    }
}

/// <summary>SMTP (Mailpit ở dev). Connection string <c>mailpit</c> dạng <c>Endpoint=smtp://host:port</c>.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "no-reply@mycloud.local";

    public string FromName { get; set; } = "MyCloud";

    /// <summary>Địa chỉ web app — để dựng link trong email.</summary>
    public string WebBaseUrl { get; set; } = "http://localhost:5173";

    public void UseConnectionString(string? connectionString)
    {
        string? endpoint = connectionString?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .FirstOrDefault(kv => kv.Length == 2 && kv[0].Equals("Endpoint", StringComparison.OrdinalIgnoreCase))?[1];
        if (endpoint is not null && Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
        {
            Host = uri.Host;
            Port = uri.Port;
        }
    }
}

public sealed class SendEmailConsumer(IOptions<SmtpOptions> options) : IConsumer<SendEmail>
{
    public async Task Consume(ConsumeContext<SendEmail> context)
    {
        SmtpOptions o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(MailboxAddress.Parse(context.Message.To));
        message.Subject = context.Message.Subject;
        message.Body = new BodyBuilder { HtmlBody = context.Message.HtmlBody, TextBody = context.Message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        // Dev: Mailpit không TLS. Production: cấu hình cổng 587 + STARTTLS (Auto tự nâng cấp nếu server hỗ trợ).
        await client.ConnectAsync(o.Host, o.Port, SecureSocketOptions.Auto, context.CancellationToken);
        await client.SendAsync(message, context.CancellationToken);
        await client.DisconnectAsync(quit: true, context.CancellationToken);
    }
}
