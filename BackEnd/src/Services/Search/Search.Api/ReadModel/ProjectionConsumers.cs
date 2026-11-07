using MassTransit;
using MongoDB.Driver;
using MyCloud.Contracts.Events.Drive;

namespace Search.Api.ReadModel;

/// <summary>
/// Chiếu event của Drive vào read model. Mọi cập nhật có điều kiện <c>Revision &lt; revision của event</c> nên
/// idempotent và bỏ qua event đến muộn. Event cập nhật tới trước <c>ItemCreated</c> (hiếm: outbox giao theo thứ tự)
/// sẽ bị bỏ qua — Phase 3 bổ sung đối soát với Drive.
/// </summary>
public sealed class ItemProjectionConsumer(SearchDb db)
    : IConsumer<ItemCreated>, IConsumer<ItemRenamed>, IConsumer<ItemMoved>, IConsumer<FileVersionAdded>
{
    private const int DuplicateKey = 11000;

    private static FilterDefinitionBuilder<ItemDocument> F => Builders<ItemDocument>.Filter;

    private static UpdateDefinitionBuilder<ItemDocument> U => Builders<ItemDocument>.Update;

    public async Task Consume(ConsumeContext<ItemCreated> context)
    {
        ItemCreated e = context.Message;
        var document = new ItemDocument
        {
            Id = e.ItemId,
            OwnerId = e.OwnerId,
            ParentId = e.ParentId,
            Kind = e.Kind == ItemKind.Folder ? "folder" : "file",
            Name = e.Name,
            NameLower = e.Name.ToLowerInvariant(),
            MimeType = e.MimeType,
            SizeBytes = e.SizeBytes,
            Revision = e.Revision,
            UpdatedAt = e.OccurredAt,
        };

        try
        {
            await db.Items.ReplaceOneAsync(
                F.Eq(x => x.Id, e.ItemId) & F.Lt(x => x.Revision, e.Revision),
                document,
                new ReplaceOptions { IsUpsert = true },
                context.CancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == DuplicateKey)
        {
            // Đã có bản mới hơn: filter không khớp nên upsert đụng _id ⇒ bỏ qua event cũ.
        }
    }

    public Task Consume(ConsumeContext<ItemRenamed> context) =>
        ApplyAsync(context.Message.ItemId, context.Message.Revision,
            U.Set(x => x.Name, context.Message.NewName)
                .Set(x => x.NameLower, context.Message.NewName.ToLowerInvariant())
                .Set(x => x.UpdatedAt, context.Message.OccurredAt),
            context.CancellationToken);

    public Task Consume(ConsumeContext<ItemMoved> context) =>
        ApplyAsync(context.Message.ItemId, context.Message.Revision,
            U.Set(x => x.ParentId, context.Message.NewParentId).Set(x => x.UpdatedAt, context.Message.OccurredAt),
            context.CancellationToken);

    public Task Consume(ConsumeContext<FileVersionAdded> context) =>
        // Version đầu tiên có cùng revision với ItemCreated ⇒ dùng ≤ để vẫn cập nhật kích thước.
        db.Items.UpdateOneAsync(
            F.Eq(x => x.Id, context.Message.ItemId) & F.Lte(x => x.Revision, context.Message.Revision),
            U.Set(x => x.SizeBytes, context.Message.SizeBytes)
                .Set(x => x.MimeType, context.Message.MimeType)
                .Set(x => x.Revision, context.Message.Revision),
            cancellationToken: context.CancellationToken);

    private Task<UpdateResult> ApplyAsync(Guid itemId, long revision, UpdateDefinition<ItemDocument> update, CancellationToken ct) =>
        db.Items.UpdateOneAsync(
            F.Eq(x => x.Id, itemId) & F.Lt(x => x.Revision, revision),
            update.Set(x => x.Revision, revision),
            cancellationToken: ct);
}
