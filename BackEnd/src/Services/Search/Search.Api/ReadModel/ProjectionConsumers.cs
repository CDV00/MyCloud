using MassTransit;
using MongoDB.Driver;
using MyCloud.Contracts.Events.Drive;

namespace Search.Api.ReadModel;

// Thay cho Consumer + EventProjection của bản cũ (find-rồi-insert không atomic, lỗi R3).
// Mỗi thao tác là upsert/delete theo Id nên tự idempotent; điều kiện UpdatedAt < OccurredAt
// bỏ qua event cũ đến sau event mới. Phase 3 chuyển sang Revision của DriveItem (docs/04 §3.5).

internal static class Upsert
{
    private const int DuplicateKey = 11000;

    public static async Task IfNewerAsync<T>(
        IMongoCollection<T> collection,
        FilterDefinition<T> byId,
        FilterDefinition<T> olderThanEvent,
        T document,
        CancellationToken cancellationToken)
    {
        try
        {
            await collection.ReplaceOneAsync(
                byId & olderThanEvent,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == DuplicateKey)
        {
            // Đã có bản mới hơn: upsert không khớp filter và đụng _id → bỏ qua event cũ.
        }
    }
}

public sealed class FolderProjectionConsumer(SearchDb db)
    : IConsumer<FolderCreated>, IConsumer<FolderUpdated>, IConsumer<FolderDeleted>
{
    public Task Consume(ConsumeContext<FolderCreated> context)
    {
        FolderCreated e = context.Message;
        return Save(new FolderDocument
        {
            Id = e.FolderId,
            OwnerId = e.OwnerId,
            Name = e.Name,
            Description = e.Description,
            ParentId = e.ParentId,
            UpdatedAt = e.OccurredAt,
        }, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<FolderUpdated> context)
    {
        FolderUpdated e = context.Message;
        return Save(new FolderDocument
        {
            Id = e.FolderId,
            OwnerId = e.OwnerId,
            Name = e.Name,
            Description = e.Description,
            ParentId = e.ParentId,
            UpdatedAt = e.OccurredAt,
        }, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<FolderDeleted> context) =>
        db.Folders.DeleteOneAsync(x => x.Id == context.Message.FolderId, context.CancellationToken);

    private Task Save(FolderDocument document, CancellationToken ct)
    {
        var f = Builders<FolderDocument>.Filter;
        return Upsert.IfNewerAsync(db.Folders, f.Eq(x => x.Id, document.Id), f.Lt(x => x.UpdatedAt, document.UpdatedAt), document, ct);
    }
}

public sealed class FileProjectionConsumer(SearchDb db)
    : IConsumer<FileCreated>, IConsumer<FileUpdated>, IConsumer<FileDeleted>
{
    public Task Consume(ConsumeContext<FileCreated> context)
    {
        FileCreated e = context.Message;
        return Save(new FileDocument
        {
            Id = e.FileId,
            OwnerId = e.OwnerId,
            Name = e.Name,
            ContentType = e.ContentType,
            SizeBytes = e.SizeBytes,
            Description = e.Description,
            FolderId = e.FolderId,
            UpdatedAt = e.OccurredAt,
        }, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<FileUpdated> context)
    {
        // Bản cũ không cập nhật Size/Type (lỗi R5) — event mới mang đủ trạng thái.
        FileUpdated e = context.Message;
        return Save(new FileDocument
        {
            Id = e.FileId,
            OwnerId = e.OwnerId,
            Name = e.Name,
            ContentType = e.ContentType,
            SizeBytes = e.SizeBytes,
            Description = e.Description,
            FolderId = e.FolderId,
            UpdatedAt = e.OccurredAt,
        }, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<FileDeleted> context) =>
        db.Files.DeleteOneAsync(x => x.Id == context.Message.FileId, context.CancellationToken);

    private Task Save(FileDocument document, CancellationToken ct)
    {
        var f = Builders<FileDocument>.Filter;
        return Upsert.IfNewerAsync(db.Files, f.Eq(x => x.Id, document.Id), f.Lt(x => x.UpdatedAt, document.UpdatedAt), document, ct);
    }
}
