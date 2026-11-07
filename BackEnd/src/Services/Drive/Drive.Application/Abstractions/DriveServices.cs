using Drive.Domain.Items;

namespace Drive.Application.Abstractions;

/// <summary>Cấu hình section <c>Drive</c>.</summary>
public sealed class DriveOptions
{
    public const string SectionName = "Drive";

    /// <summary>Số version giữ lại cho mỗi file (tính cả version hiện tại); cũ hơn bị xoá, trả quota.</summary>
    public int MaxVersionsPerFile { get; set; } = 100;

    /// <summary>Item trong thùng rác quá thời gian này bị xoá vĩnh viễn.</summary>
    public TimeSpan TrashRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Số item tối đa khi sao chép một thư mục trong một yêu cầu.</summary>
    public int MaxCopyItems { get; set; } = 500;

    /// <summary>Số item (file + thư mục) tối đa trong một tệp zip.</summary>
    public int MaxArchiveItems { get; set; } = 5_000;

    /// <summary>Tổng kích thước tối đa các file trong một tệp zip.</summary>
    public long MaxArchiveBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>Số thư mục tối đa tạo trong một yêu cầu tải thư mục lên.</summary>
    public int MaxFolderTreeSize { get; set; } = 1_000;

    /// <summary>Số item xoá vĩnh viễn trong một transaction (mỗi lô một event <c>ItemsPurged</c>).</summary>
    public int PurgeBatchSize { get; set; } = 500;
}

/// <summary>Section <c>Collaboration</c>: secret ký vé vào phòng realtime (dùng chung với Notification).</summary>
public sealed class CollaborationOptions
{
    public const string SectionName = "Collaboration";

    public string Secret { get; set; } = string.Empty;

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(12);
}

public sealed record LiveRoomTicket(string Token, DateTimeOffset ExpiresAt);

/// <summary>Vé vào phòng realtime của một item (sau khi đã kiểm tra quyền xem).</summary>
public interface ILiveRoomTokens
{
    LiveRoomTicket Issue(Guid itemId, Guid userId);
}

/// <summary>Tin nhắn mới — Infrastructure phát thành event <c>ItemCommentPosted</c>.</summary>
public sealed record CommentPostedNotice(
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
    IReadOnlyList<Guid> Mentions);

/// <summary>Phát event trao đổi / lịch sử (qua outbox).</summary>
public interface ICollaborationEvents
{
    Task CommentPostedAsync(CommentPostedNotice notice, CancellationToken ct);

    Task HistoryChangedAsync(Guid itemId, Guid ownerId, string kind, CancellationToken ct);
}

/// <summary>Xoá vĩnh viễn: xoá cả cây con theo lô, mỗi lô publish <c>ItemsPurged</c> (kèm blob của mọi version).</summary>
public interface ITrashPurger
{
    /// <summary>Xoá <paramref name="item"/> và mọi con cháu (kể cả nhóm thùng rác khác nằm bên trong); trả số item đã xoá.</summary>
    Task<int> PurgeAsync(DriveItem item, CancellationToken ct);
}

public static class AccessActions
{
    public const string Open = "open";
    public const string Download = "download";
}

/// <summary>Ghi nhận người dùng mở / tải file ⇒ event <c>ItemAccessed</c> (tối đa 1 lần / người / item / 5 phút).</summary>
public interface IItemAccessLog
{
    Task RecordAsync(DriveItem item, Guid userId, string action, CancellationToken ct);
}
