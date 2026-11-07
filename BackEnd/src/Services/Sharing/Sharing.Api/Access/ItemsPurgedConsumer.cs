using MassTransit;
using MyCloud.Contracts.Events.Drive;

namespace Sharing.Api.Access;

/// <summary>Item bị xoá vĩnh viễn ⇒ quyền và link của chúng không còn ý nghĩa.</summary>
public sealed class ItemsPurgedConsumer(AccessService access) : IConsumer<ItemsPurged>
{
    public Task Consume(ConsumeContext<ItemsPurged> context) => access.ForgetItemsAsync(context.Message.ItemIds, context.CancellationToken);
}
