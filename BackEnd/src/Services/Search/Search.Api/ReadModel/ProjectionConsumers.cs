using MassTransit;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCloud.Contracts.Events.Drive;

namespace Search.Api.ReadModel;

/// <summary>
/// Chiếu event của Drive vào read model (items, recent, activity). Idempotent theo cách dựng, không cần transaction
/// hay inbox Mongo:
/// <list type="bullet">
/// <item>Cập nhật item có điều kiện <c>Revision &lt; revision của event</c> ⇒ bỏ qua event cũ / giao lại.</item>
/// <item>Con cháu không được cập nhật theo nội dung event mà <b>đồng bộ theo trạng thái hiện tại của thư mục cha</b>
/// (sau khi đã áp dụng điều kiện revision) ⇒ event đến sai thứ tự vẫn hội tụ về đúng.</item>
/// <item>Activity có Id = id event; recent là upsert theo thời gian mới nhất.</item>
/// </list>
/// Event cùng chủ sở hữu được xử lý tuần tự (partition theo OwnerId — xem definition bên dưới).
/// </summary>
public sealed class ItemProjectionConsumer(SearchDb db) :
    IConsumer<ItemCreated>,
    IConsumer<ItemRenamed>,
    IConsumer<ItemMoved>,
    IConsumer<ItemTrashed>,
    IConsumer<ItemRestored>,
    IConsumer<ItemsPurged>,
    IConsumer<FileVersionAdded>,
    IConsumer<ItemAccessed>
{
    private const int DuplicateKey = 11000;

    private static FilterDefinitionBuilder<ItemDocument> F => Builders<ItemDocument>.Filter;

    private static UpdateDefinitionBuilder<ItemDocument> U => Builders<ItemDocument>.Update;

    public async Task Consume(ConsumeContext<ItemCreated> context)
    {
        ItemCreated e = context.Message;
        bool isFolder = e.Kind == ItemKind.Folder;
        var document = new ItemDocument
        {
            Id = e.ItemId,
            OwnerId = e.OwnerId,
            ParentId = e.ParentId,
            AncestorIds = [.. e.AncestorIds],
            Kind = isFolder ? "folder" : "file",
            Name = e.Name,
            NameLower = e.Name.ToLowerInvariant(),
            NameFolded = TextFolding.Fold(e.Name),
            Category = Categories.Of(isFolder, e.MimeType),
            MimeType = e.MimeType,
            SizeBytes = e.SizeBytes,
            Revision = e.Revision,
            UpdatedAt = e.OccurredAt,
            ModifiedBy = e.CreatedBy,
        };

        await IgnoreDuplicateAsync(() => db.Items.ReplaceOneAsync(
            F.Eq(x => x.Id, e.ItemId) & F.Lt(x => x.Revision, e.Revision),
            document,
            new ReplaceOptions { IsUpsert = true },
            context.CancellationToken));

        if (e.ParentId is not null) // thư mục gốc được tạo tự động, không phải hoạt động của người dùng
        {
            await RecordAsync(context, new ActivityDocument
            {
                ItemId = e.ItemId,
                OwnerId = e.OwnerId,
                AncestorIds = [.. e.AncestorIds],
                ActorId = e.CreatedBy,
                Action = "created",
                Kind = document.Kind,
                Name = e.Name,
            });
        }
    }

    public async Task Consume(ConsumeContext<ItemRenamed> context)
    {
        ItemRenamed e = context.Message;
        ItemDocument? before = await db.Items.FindOneAndUpdateAsync(
            F.Eq(x => x.Id, e.ItemId) & F.Lt(x => x.Revision, e.Revision),
            U.Set(x => x.Name, e.NewName)
                .Set(x => x.NameLower, e.NewName.ToLowerInvariant())
                .Set(x => x.NameFolded, TextFolding.Fold(e.NewName))
                .Set(x => x.UpdatedAt, e.OccurredAt)
                .Set(x => x.ModifiedBy, e.By)
                .Set(x => x.Revision, e.Revision),
            new FindOneAndUpdateOptions<ItemDocument> { ReturnDocument = ReturnDocument.Before },
            context.CancellationToken);

        ItemDocument? current = before ?? await FindAsync(e.ItemId, context.CancellationToken);
        await RecordAsync(context, new ActivityDocument
        {
            ItemId = e.ItemId,
            OwnerId = e.OwnerId,
            AncestorIds = current?.AncestorIds ?? [],
            ActorId = e.By,
            Action = "renamed",
            Kind = current?.Kind ?? string.Empty,
            Name = e.NewName,
            PreviousName = before?.Name,
        });
    }

    public async Task Consume(ConsumeContext<ItemMoved> context)
    {
        ItemMoved e = context.Message;
        await db.Items.UpdateOneAsync(
            F.Eq(x => x.Id, e.ItemId) & F.Lt(x => x.Revision, e.Revision),
            U.Set(x => x.ParentId, e.NewParentId)
                .Set(x => x.AncestorIds, [.. e.NewAncestorIds])
                .Set(x => x.UpdatedAt, e.OccurredAt)
                .Set(x => x.ModifiedBy, e.By)
                .Set(x => x.Revision, e.Revision),
            cancellationToken: context.CancellationToken);
        ItemDocument? current = await SyncDescendantsAsync(e.ItemId, context.CancellationToken);

        await RecordAsync(context, new ActivityDocument
        {
            ItemId = e.ItemId,
            OwnerId = e.OwnerId,
            AncestorIds = [.. e.NewAncestorIds],
            ActorId = e.By,
            Action = "moved",
            Kind = current?.Kind ?? string.Empty,
            Name = current?.Name ?? string.Empty,
            FromFolder = await FolderNameAsync(e.OldParentId, context.CancellationToken),
            ToFolder = await FolderNameAsync(e.NewParentId, context.CancellationToken),
        });
    }

    public async Task Consume(ConsumeContext<ItemTrashed> context)
    {
        ItemTrashed e = context.Message;
        await db.Items.UpdateOneAsync(
            F.Eq(x => x.Id, e.ItemId) & F.Lt(x => x.Revision, e.Revision),
            U.Set(x => x.Trashed, true).Set(x => x.TrashedAt, e.OccurredAt).Set(x => x.Revision, e.Revision),
            cancellationToken: context.CancellationToken);

        ItemDocument? current = await FindAsync(e.ItemId, context.CancellationToken);
        await RecordAsync(context, new ActivityDocument
        {
            ItemId = e.ItemId,
            OwnerId = e.OwnerId,
            AncestorIds = current?.AncestorIds ?? [],
            ActorId = e.By,
            Action = "trashed",
            Kind = current?.Kind ?? string.Empty,
            Name = current?.Name ?? string.Empty,
        });
    }

    public async Task Consume(ConsumeContext<ItemRestored> context)
    {
        ItemRestored e = context.Message;
        await db.Items.UpdateOneAsync(
            F.Eq(x => x.Id, e.ItemId) & F.Lt(x => x.Revision, e.Revision),
            U.Set(x => x.Trashed, false)
                .Set(x => x.TrashedAt, null)
                .Set(x => x.ParentId, e.ParentId)
                .Set(x => x.AncestorIds, [.. e.AncestorIds])
                .Set(x => x.Name, e.Name)
                .Set(x => x.NameLower, e.Name.ToLowerInvariant())
                .Set(x => x.NameFolded, TextFolding.Fold(e.Name))
                .Set(x => x.Revision, e.Revision),
            cancellationToken: context.CancellationToken);
        ItemDocument? current = await SyncDescendantsAsync(e.ItemId, context.CancellationToken);

        await RecordAsync(context, new ActivityDocument
        {
            ItemId = e.ItemId,
            OwnerId = e.OwnerId,
            AncestorIds = [.. e.AncestorIds],
            ActorId = e.By,
            Action = "restored",
            Kind = current?.Kind ?? string.Empty,
            Name = e.Name,
        });
    }

    public async Task Consume(ConsumeContext<ItemsPurged> context)
    {
        ItemsPurged e = context.Message;
        CancellationToken ct = context.CancellationToken;
        await db.Items.DeleteManyAsync(F.In(x => x.Id, e.ItemIds) & F.Eq(x => x.OwnerId, e.OwnerId), ct);
        await db.Recent.DeleteManyAsync(Builders<RecentDocument>.Filter.In(x => x.ItemId, e.ItemIds), ct);
        await db.Activity.DeleteManyAsync(
            Builders<ActivityDocument>.Filter.In(x => x.ItemId, e.ItemIds) & Builders<ActivityDocument>.Filter.Eq(x => x.OwnerId, e.OwnerId),
            ct);
        await db.Grants.DeleteManyAsync(Builders<AccessGrantDocument>.Filter.In(x => x.ItemId, e.ItemIds), ct);
    }

    public async Task Consume(ConsumeContext<FileVersionAdded> context)
    {
        FileVersionAdded e = context.Message;
        // Version đầu tiên có cùng revision với ItemCreated ⇒ dùng ≤ để vẫn cập nhật kích thước.
        await db.Items.UpdateOneAsync(
            F.Eq(x => x.Id, e.ItemId) & F.Lte(x => x.Revision, e.Revision),
            U.Set(x => x.SizeBytes, e.SizeBytes)
                .Set(x => x.MimeType, e.MimeType)
                .Set(x => x.Category, Categories.Of(isFolder: false, e.MimeType))
                .Set(x => x.UpdatedAt, e.OccurredAt)
                .Set(x => x.ModifiedBy, e.By)
                .Set(x => x.Revision, e.Revision),
            cancellationToken: context.CancellationToken);

        await TouchRecentAsync(e.By, e.ItemId, e.VersionNo == 1 ? "upload" : "edit", e.OccurredAt, context.CancellationToken);
        if (e.VersionNo > 1)
        {
            ItemDocument? current = await FindAsync(e.ItemId, context.CancellationToken);
            await RecordAsync(context, new ActivityDocument
            {
                ItemId = e.ItemId,
                OwnerId = e.OwnerId,
                AncestorIds = current?.AncestorIds ?? [],
                ActorId = e.By,
                Action = "version_added",
                Kind = "file",
                Name = current?.Name ?? string.Empty,
                VersionNo = e.VersionNo,
            });
        }
    }

    public Task Consume(ConsumeContext<ItemAccessed> context) =>
        TouchRecentAsync(context.Message.UserId, context.Message.ItemId, context.Message.Action, context.Message.OccurredAt, context.CancellationToken);

    private async Task TouchRecentAsync(Guid userId, Guid itemId, string action, DateTimeOffset at, CancellationToken ct)
    {
        var f = Builders<RecentDocument>.Filter;
        string key = RecentDocument.KeyOf(userId, itemId);
        await IgnoreDuplicateAsync(() => db.Recent.UpdateOneAsync(
            f.Eq(x => x.Id, key) & f.Lt(x => x.LastAccessAt, at), // chỉ tiến về phía trước
            Builders<RecentDocument>.Update
                .Set(x => x.UserId, userId)
                .Set(x => x.ItemId, itemId)
                .Set(x => x.LastAccessAt, at)
                .Set(x => x.Action, action),
            new UpdateOptions { IsUpsert = true },
            ct));
    }

    /// <summary>
    /// Đặt <c>AncestorIds</c> của mọi con cháu theo trạng thái HIỆN TẠI của <paramref name="itemId"/>:
    /// <c>[...tổ tiên của nó, nó, ...phần sau nó trong mảng cũ]</c> — một câu update pipeline.
    /// </summary>
    private async Task<ItemDocument?> SyncDescendantsAsync(Guid itemId, CancellationToken ct)
    {
        ItemDocument? item = await FindAsync(itemId, ct);
        if (item is null || item.Kind != "folder")
        {
            return item;
        }

        var prefix = new BsonArray(item.AncestorIds.Append(item.Id).Select(ToBson));
        var position = new BsonDocument("$add", new BsonArray { new BsonDocument("$indexOfArray", new BsonArray { "$AncestorIds", ToBson(itemId) }), 1 });
        var rest = new BsonDocument("$slice", new BsonArray { "$AncestorIds", position, new BsonDocument("$size", "$AncestorIds") });
        var set = new BsonDocument("$set", new BsonDocument(
            nameof(ItemDocument.AncestorIds), new BsonDocument("$concatArrays", new BsonArray { prefix, rest })));

        await db.Items.UpdateManyAsync(
            F.AnyEq(x => x.AncestorIds, itemId),
            new PipelineUpdateDefinition<ItemDocument>(new[] { set }),
            cancellationToken: ct);
        return item;
    }

    private Task<ItemDocument?> FindAsync(Guid itemId, CancellationToken ct) =>
        db.Items.Find(F.Eq(x => x.Id, itemId)).FirstOrDefaultAsync(ct)!;

    private async Task<string?> FolderNameAsync(Guid folderId, CancellationToken ct) =>
        await FindAsync(folderId, ct) is { ParentId: not null } folder ? folder.Name : null;

    private Task RecordAsync<T>(ConsumeContext<T> context, ActivityDocument activity)
        where T : MyCloud.Contracts.Events.IntegrationEvent
    {
        activity.Id = context.Message.EventId;
        activity.At = context.Message.OccurredAt;
        return IgnoreDuplicateAsync(() => db.Activity.InsertOneAsync(activity, cancellationToken: context.CancellationToken));
    }

    /// <summary>Upsert / insert đụng _id đã có ⇒ bản mới hơn đã tồn tại hoặc event được giao lại ⇒ bỏ qua.</summary>
    private static async Task IgnoreDuplicateAsync(Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == DuplicateKey)
        {
        }
    }

    private static BsonBinaryData ToBson(Guid id) => new(id, GuidRepresentation.Standard);
}

/// <summary>
/// Event của cùng một người dùng đi qua cùng một partition ⇒ xử lý tuần tự đúng thứ tự giao (outbox của Drive giao
/// theo thứ tự ghi), người dùng khác nhau vẫn song song.
/// </summary>
public sealed class ItemProjectionConsumerDefinition : ConsumerDefinition<ItemProjectionConsumer>
{
    private const int Partitions = 16;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ItemProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        IPartitioner partitioner = endpointConfigurator.CreatePartitioner(Partitions);
        consumerConfigurator.Message<ItemCreated>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
        consumerConfigurator.Message<ItemRenamed>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
        consumerConfigurator.Message<ItemMoved>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
        consumerConfigurator.Message<ItemTrashed>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
        consumerConfigurator.Message<ItemRestored>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
        consumerConfigurator.Message<ItemsPurged>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
        consumerConfigurator.Message<FileVersionAdded>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
        consumerConfigurator.Message<ItemAccessed>(m => m.UsePartitioner(partitioner, c => c.Message.OwnerId));
    }
}
