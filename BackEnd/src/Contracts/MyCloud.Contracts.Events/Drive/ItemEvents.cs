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

/// <summary>
/// Nội dung mới của file — Storage "claim" blob (ADR-006); Media tạo thumbnail (Phase 5); Search ghi lịch sử.
/// <c>Source</c>: upload | edit (trình sửa) | restore (khôi phục <c>RestoredFromVersionNo</c>) | copy.
/// </summary>
public sealed record FileVersionAdded(
    Guid ItemId,
    Guid OwnerId,
    Guid VersionId,
    int VersionNo,
    Guid BlobId,
    long SizeBytes,
    string MimeType,
    long Revision,
    Guid By,
    string Source = "upload",
    int? RestoredFromVersionNo = null) : IntegrationEvent;

/// <summary>
/// Item (và cả cây con nếu là thư mục) vào thùng rác. Chỉ mang item gốc của nhóm: consumer suy ra các item con bị ẩn
/// qua <c>AncestorIds</c> nên không phải gửi danh sách hàng nghìn id, và chỉ một document cần kiểm tra Revision.
/// </summary>
public sealed record ItemTrashed(Guid ItemId, Guid OwnerId, long Revision, Guid By) : IntegrationEvent;

/// <summary>
/// Khôi phục cả nhóm đã xoá cùng lúc. <c>ParentId</c>/<c>AncestorIds</c> có thể khác trước (thư mục cha đã nằm trong
/// thùng rác ⇒ khôi phục về "Drive của tôi"), tên có thể đã được đổi để không trùng.
/// </summary>
public sealed record ItemRestored(
    Guid ItemId,
    Guid OwnerId,
    Guid ParentId,
    IReadOnlyList<Guid> AncestorIds,
    string Name,
    long Revision,
    Guid By) : IntegrationEvent;

/// <summary>Đã xoá vĩnh viễn (theo lô tối đa 500 item). Storage xoá object + trả quota; Search xoá index.</summary>
public sealed record ItemsPurged(Guid OwnerId, IReadOnlyList<Guid> ItemIds, IReadOnlyList<Guid> BlobIds) : IntegrationEvent;

/// <summary>Version cũ bị xoá (giới hạn số version giữ lại, hoặc người dùng xoá) — Storage xoá blob, trả quota.</summary>
public sealed record FileVersionsDeleted(
    Guid ItemId,
    Guid OwnerId,
    IReadOnlyList<Guid> VersionIds,
    IReadOnlyList<Guid> BlobIds,
    long Revision) : IntegrationEvent;

/// <summary>Người dùng mở / tải file — cho trang "Gần đây". Drive gộp tối đa 1 event / người / item / 5 phút.</summary>
public sealed record ItemAccessed(Guid ItemId, Guid OwnerId, Guid UserId, string Action) : IntegrationEvent
{
    public const string Open = "open";
    public const string Download = "download";
}

/// <summary>
/// Tin nhắn mới trong trao đổi về một tệp (ADR-017). Notification báo cho <c>Recipients</c> (chủ tệp, người tạo phiên bản
/// được góp ý, người trong luồng, người được nhắc tên — trừ người viết) và đẩy tới "phòng" realtime của tệp.
/// </summary>
public sealed record ItemCommentPosted(
    Guid CommentId,
    Guid ItemId,
    Guid OwnerId,
    string ItemName,
    Guid? VersionId,
    int? VersionNo,
    Guid? ParentId,
    Guid AuthorId,
    string AuthorName,
    string Excerpt,
    IReadOnlyList<Guid> Recipients,
    IReadOnlyList<Guid> Mentions) : IntegrationEvent;

/// <summary>Lịch sử / trao đổi của tệp đổi (sửa, xoá, đánh dấu đã xử lý tin nhắn; sửa ghi chú phiên bản) — chỉ để làm mới
/// màn hình của người đang xem tệp.</summary>
public sealed record ItemHistoryChanged(Guid ItemId, Guid OwnerId, string Kind) : IntegrationEvent;
