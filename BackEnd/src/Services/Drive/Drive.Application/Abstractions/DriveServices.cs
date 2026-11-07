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

    /// <summary>Số item xoá vĩnh viễn trong một transaction (mỗi lô một event <c>ItemsPurged</c>).</summary>
    public int PurgeBatchSize { get; set; } = 500;
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
