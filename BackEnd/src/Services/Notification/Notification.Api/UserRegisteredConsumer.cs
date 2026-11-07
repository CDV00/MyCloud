using MassTransit;
using MyCloud.Contracts.Events.Identity;

namespace Notification.Api;

/// <summary>
/// Phase 0: chỉ ghi log để kiểm chứng đường đi Identity → outbox → RabbitMQ → Notification.
/// Phase 4: lưu thông tin liên hệ + gửi email chào mừng (Mailpit ở môi trường dev).
/// </summary>
public sealed class UserRegisteredConsumer(ILogger<UserRegisteredConsumer> logger) : IConsumer<UserRegistered>
{
    public Task Consume(ConsumeContext<UserRegistered> context)
    {
        // Không log email (dữ liệu cá nhân).
        logger.LogInformation("Received UserRegistered for user {UserId} (event {EventId})",
            context.Message.UserId, context.Message.EventId);
        return Task.CompletedTask;
    }
}
