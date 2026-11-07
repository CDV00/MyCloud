using Drive.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.Contracts.Events.Media;

namespace Drive.Infrastructure.Messaging;

/// <summary>
/// Media Worker đã tạo thumbnail ⇒ đánh dấu item, chỉ khi version đó vẫn là version hiện tại (thumbnail của version cũ
/// đến muộn bị bỏ qua). Không tăng Revision và không phát event: thumbnail không phải thay đổi của người dùng, và câu
/// UPDATE có điều kiện không xung đột với người đang sửa item.
/// </summary>
public sealed class ThumbnailGeneratedConsumer(DriveDbContext db) : IConsumer<ThumbnailGenerated>
{
    public Task Consume(ConsumeContext<ThumbnailGenerated> context)
    {
        ThumbnailGenerated e = context.Message;
        return db.Items
            .Where(i => i.Id == e.ItemId && i.CurrentVersionId == e.VersionId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ThumbnailVersionId, e.VersionId), context.CancellationToken);
    }
}
