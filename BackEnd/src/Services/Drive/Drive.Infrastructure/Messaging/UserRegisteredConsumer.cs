using Drive.Domain;
using Drive.Domain.Items;
using MassTransit;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.Contracts.Events.Identity;

namespace Drive.Infrastructure.Messaging;

/// <summary>Người dùng mới ⇒ tạo thư mục gốc "Drive của tôi". Idempotent: đã có thì bỏ qua (và có inbox chống trùng).</summary>
public sealed class UserRegisteredConsumer(IDriveItemRepository items, IUnitOfWork unitOfWork) : IConsumer<UserRegistered>
{
    public async Task Consume(ConsumeContext<UserRegistered> context)
    {
        Guid userId = context.Message.UserId;
        if (await items.FindRootAsync(userId, context.CancellationToken) is null)
        {
            items.Add(DriveItem.CreateRoot(Guid.CreateVersion7(), userId));
            try
            {
                await unitOfWork.SaveChangesAsync(context.CancellationToken);
            }
            catch (DriveException.RootCreatedConcurrently)
            {
                // Người dùng đã mở Drive (GET /items/root) và root được tạo ở đó — đúng kết quả mong muốn.
            }
        }
    }
}
