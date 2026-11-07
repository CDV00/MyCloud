namespace MyCloud.Contracts.Events.Drive;

// Event chuyển tiếp — xem ghi chú ở FolderEvents.cs.

public sealed record FileCreated(
    Guid FileId,
    string Name,
    string ContentType,
    long SizeBytes,
    string Description,
    Guid FolderId,
    Guid CreatedBy) : IntegrationEvent;

public sealed record FileUpdated(
    Guid FileId,
    string Name,
    string ContentType,
    long SizeBytes,
    string Description,
    Guid FolderId,
    Guid ModifiedBy) : IntegrationEvent;

public sealed record FileDeleted(Guid FileId) : IntegrationEvent;
