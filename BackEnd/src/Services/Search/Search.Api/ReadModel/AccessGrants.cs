using MassTransit;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MyCloud.Contracts.Events.Sharing;

namespace Search.Api.ReadModel;

/// <summary>Quyền trực tiếp được cấp cho một người (docs/03 §6 <c>access_grants</c>) — gốc của phần cây họ thấy.</summary>
public sealed class AccessGrantDocument
{
    /// <summary>Id quyền bên Sharing.</summary>
    [BsonId]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ItemId { get; set; }

    public Guid OwnerId { get; set; }

    public string Role { get; set; } = string.Empty;

    public Guid SharedBy { get; set; }

    public DateTimeOffset SharedAt { get; set; }
}

/// <summary>
/// Chiếu event của Sharing. Event của cùng một quyền xử lý tuần tự (partition theo PermissionId); thu hồi xoá hẳn
/// document nên event cũ đến sau (hiếm) chỉ có thể tạo lại quyền đã thu hồi nếu thứ tự bị đảo — outbox giao theo thứ tự.
/// </summary>
public sealed class AccessGrantConsumer(SearchDb db) :
    IConsumer<ItemShared>,
    IConsumer<ShareRoleChanged>,
    IConsumer<ShareRevoked>
{
    private static FilterDefinitionBuilder<AccessGrantDocument> F => Builders<AccessGrantDocument>.Filter;

    public Task Consume(ConsumeContext<ItemShared> context)
    {
        ItemShared e = context.Message;
        return db.Grants.ReplaceOneAsync(
            F.Eq(x => x.Id, e.PermissionId),
            new AccessGrantDocument
            {
                Id = e.PermissionId,
                UserId = e.GranteeId,
                ItemId = e.ItemId,
                OwnerId = e.OwnerId,
                Role = e.Role,
                SharedBy = e.SharedBy,
                SharedAt = e.OccurredAt,
            },
            new ReplaceOptions { IsUpsert = true },
            context.CancellationToken);
    }

    public Task Consume(ConsumeContext<ShareRoleChanged> context) =>
        db.Grants.UpdateOneAsync(
            F.Eq(x => x.Id, context.Message.PermissionId),
            Builders<AccessGrantDocument>.Update.Set(x => x.Role, context.Message.Role),
            cancellationToken: context.CancellationToken);

    public Task Consume(ConsumeContext<ShareRevoked> context) =>
        db.Grants.DeleteOneAsync(F.Eq(x => x.Id, context.Message.PermissionId), context.CancellationToken);
}

public sealed class AccessGrantConsumerDefinition : ConsumerDefinition<AccessGrantConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<AccessGrantConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        IPartitioner partitioner = endpointConfigurator.CreatePartitioner(8);
        consumerConfigurator.Message<ItemShared>(m => m.UsePartitioner(partitioner, c => c.Message.PermissionId));
        consumerConfigurator.Message<ShareRoleChanged>(m => m.UsePartitioner(partitioner, c => c.Message.PermissionId));
        consumerConfigurator.Message<ShareRevoked>(m => m.UsePartitioner(partitioner, c => c.Message.PermissionId));
    }
}

/// <summary>
/// Điều kiện "người dùng thấy item này" dùng chung cho tìm kiếm, Gần đây, Được chia sẻ, hoạt động (docs/03 §6):
/// của mình HOẶC nằm trong phần cây được chia sẻ trực tiếp; không phải thư mục gốc; không ở thùng rác (kể cả nằm trong
/// thư mục đã xoá).
/// </summary>
public static class Visibility
{
    private const int MaxIds = 5_000;

    public static async Task<(FilterDefinition<ItemDocument> Filter, IReadOnlyList<AccessGrantDocument> Grants)> ForAsync(
        SearchDb db, Guid me, CancellationToken ct)
    {
        var f = Builders<ItemDocument>.Filter;
        List<AccessGrantDocument> grants = await db.Grants
            .Find(Builders<AccessGrantDocument>.Filter.Eq(x => x.UserId, me))
            .Limit(MaxIds)
            .ToListAsync(ct);
        Guid[] grantedItems = grants.Select(g => g.ItemId).Distinct().ToArray();
        Guid[] owners = grants.Select(g => g.OwnerId).Append(me).Distinct().ToArray();

        List<Guid> hiddenRoots = await db.Items
            .Find(f.In(x => x.OwnerId, owners) & f.Eq(x => x.Trashed, true))
            .Project(x => x.Id)
            .Limit(MaxIds)
            .ToListAsync(ct);

        FilterDefinition<ItemDocument> reachable = grantedItems.Length == 0
            ? f.Eq(x => x.OwnerId, me)
            : f.Eq(x => x.OwnerId, me) | f.In(x => x.Id, grantedItems) | f.AnyIn(x => x.AncestorIds, grantedItems);
        FilterDefinition<ItemDocument> visible = reachable & f.Ne(x => x.ParentId, null) & f.Ne(x => x.Trashed, true);
        return (hiddenRoots.Count == 0 ? visible : visible & f.AnyNin(x => x.AncestorIds, hiddenRoots), grants);
    }
}
