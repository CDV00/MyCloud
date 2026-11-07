using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Search.Api.ReadModel;

public sealed class FolderDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    /// <summary>Thời điểm của event mới nhất đã áp dụng — dùng để bỏ qua event đến muộn/sai thứ tự.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FileDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid FolderId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SearchDb(IMongoDatabase database)
{
    public IMongoCollection<FolderDocument> Folders { get; } = database.GetCollection<FolderDocument>("folders");

    public IMongoCollection<FileDocument> Files { get; } = database.GetCollection<FileDocument>("files");

    /// <summary>Guid lưu dạng UUID chuẩn; DateTimeOffset lưu dạng document có thể so sánh.</summary>
    public static void RegisterSerializers()
    {
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        await Folders.Indexes.CreateOneAsync(
            new CreateIndexModel<FolderDocument>(Builders<FolderDocument>.IndexKeys.Ascending(x => x.ParentId)),
            cancellationToken: cancellationToken);
        await Files.Indexes.CreateOneAsync(
            new CreateIndexModel<FileDocument>(Builders<FileDocument>.IndexKeys.Ascending(x => x.FolderId)),
            cancellationToken: cancellationToken);
    }
}
