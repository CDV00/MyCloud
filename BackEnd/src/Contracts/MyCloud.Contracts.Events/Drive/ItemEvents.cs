namespace MyCloud.Contracts.Events.Drive;

// Event của aggregate DriveItem (docs/04 §3.2). Mọi event mang Revision của item: consumer bỏ qua event
// có Revision ≤ bản đã áp dụng (event đến muộn / sai thứ tự / bị giao lại).

public enum ItemKind
{
    Folder = 1,
    File = 2,
}

public sealed record ItemCreated(
    Guid ItemId,
    Guid OwnerId,
    Guid? ParentId,
    IReadOnlyList<Guid> AncestorIds,
    ItemKind Kind,
    string Name,
    string? MimeType,
    long SizeBytes,
    long Revision,
    Guid CreatedBy) : IntegrationEvent;

public sealed record ItemRenamed(Guid ItemId, Guid OwnerId, string NewName, long Revision, Guid By) : IntegrationEvent;

public sealed record ItemMoved(
    Guid ItemId,
    Guid OwnerId,
    Guid OldParentId,
    Guid NewParentId,
    IReadOnlyList<Guid> NewAncestorIds,
    long Revision,
    Guid By) : IntegrationEvent;

/// <summary>Nội dung mới của file — Storage "claim" blob (ADR-006); Media tạo thumbnail (Phase 5).</summary>
public sealed record FileVersionAdded(
    Guid ItemId,
    Guid OwnerId,
    Guid VersionId,
    int VersionNo,
    Guid BlobId,
    long SizeBytes,
    string MimeType,
    long Revision,
    Guid By) : IntegrationEvent;
