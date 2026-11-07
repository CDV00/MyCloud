namespace MyCloud.Contracts.Events.Drive;

// Event chuyển tiếp của mô hình Folder/File hiện tại.
// Phase 2 thay bằng ItemCreated/ItemRenamed/ItemMoved/... của aggregate DriveItem (docs/04 §3.2).

public sealed record FolderCreated(Guid FolderId, string Name, string Description, Guid? ParentId, Guid CreatedBy) : IntegrationEvent;

public sealed record FolderUpdated(Guid FolderId, string Name, string Description, Guid? ParentId, Guid ModifiedBy) : IntegrationEvent;

public sealed record FolderDeleted(Guid FolderId) : IntegrationEvent;
