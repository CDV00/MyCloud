using Drive.Domain.Items;
using Drive.Domain.Uploads;

namespace Drive.Domain;

public enum ChildOrder
{
    Name,
    ModifiedAt,
    Size,
}

public interface IDriveItemRepository
{
    Task<DriveItem?> FindAsync(Guid id, CancellationToken ct);

    Task<DriveItem?> FindRootAsync(Guid ownerId, CancellationToken ct);

    Task<IReadOnlyList<DriveItem>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Thư mục <paramref name="parentId"/> đã có item với khoá tên này chưa (bỏ qua <paramref name="exceptItemId"/>).</summary>
    Task<bool> NameExistsAsync(Guid parentId, string nameKey, Guid? exceptItemId, CancellationToken ct);

    /// <summary>Các khoá tên trong thư mục bắt đầu bằng <paramref name="keyPrefix"/> — để chọn tên "(n)" chưa dùng.</summary>
    Task<IReadOnlySet<string>> NameKeysStartingWithAsync(Guid parentId, string keyPrefix, CancellationToken ct);

    /// <summary>Nội dung thư mục, thư mục đứng trước file.</summary>
    Task<IReadOnlyList<DriveItem>> ListChildrenAsync(Guid parentId, ChildOrder order, bool descending, int skip, int take, CancellationToken ct);

    void Add(DriveItem item);

    /// <summary>Cập nhật đường dẫn của mọi con cháu — chạy cùng transaction với lần lưu kế tiếp.</summary>
    void MoveSubtree(SubtreeMove move);
}

public interface IPendingUploadRepository
{
    Task<PendingUpload?> FindAsync(Guid id, CancellationToken ct);

    void Add(PendingUpload upload);
}
