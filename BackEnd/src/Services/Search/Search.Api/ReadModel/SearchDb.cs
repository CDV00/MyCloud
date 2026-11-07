using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Search.Api.ReadModel;

/// <summary>Bản sao rút gọn của DriveItem để tìm kiếm (Phase 3 thêm "Gần đây", gắn sao, item được chia sẻ).</summary>
public sealed class ItemDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>"folder" | "file".</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Tên chữ thường để tìm không phân biệt hoa thường.</summary>
    public string NameLower { get; set; } = string.Empty;

    public string? MimeType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Revision của item trong Drive — chỉ áp dụng event có revision mới hơn (bỏ qua event cũ/lặp).</summary>
    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SearchDb(IMongoDatabase database)
{
    public IMongoCollection<ItemDocument> Items { get; } = database.GetCollection<ItemDocument>("items");

    /// <summary>Guid lưu dạng UUID chuẩn; DateTimeOffset lưu dạng ngày giờ so sánh được.</summary>
    public static void RegisterSerializers()
    {
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var keys = Builders<ItemDocument>.IndexKeys;
        await Items.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ItemDocument>(keys.Ascending(x => x.OwnerId).Ascending(x => x.ParentId)),
                new CreateIndexModel<ItemDocument>(keys.Ascending(x => x.OwnerId).Ascending(x => x.NameLower)),
            ],
            cancellationToken);

        // Collection của Phase 0–1 (mô hình Folder/File) không còn dùng.
        await database.DropCollectionAsync("folders", cancellationToken);
        await database.DropCollectionAsync("files", cancellationToken);
    }
}
