using Drive.Domain.Items;
using Drive.Domain.Stars;
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

    /// <summary>
    /// Thư mục <paramref name="parentId"/> đã có item <b>chưa bị xoá</b> với khoá tên này chưa (bỏ qua
    /// <paramref name="exceptItemId"/>). Item trong thùng rác không giữ tên.
    /// </summary>
    Task<bool> NameExistsAsync(Guid parentId, string nameKey, Guid? exceptItemId, CancellationToken ct);

    /// <summary>Các khoá tên (item chưa bị xoá) trong thư mục bắt đầu bằng <paramref name="keyPrefix"/> — để chọn tên "(n)".</summary>
    Task<IReadOnlySet<string>> NameKeysStartingWithAsync(Guid parentId, string keyPrefix, CancellationToken ct);

    /// <summary>
    /// Nội dung thư mục, thư mục đứng trước file. <paramref name="trashGroup"/> null ⇒ chỉ item chưa bị xoá; khác null
    /// (đang xem thư mục trong thùng rác) ⇒ các item bị xoá cùng nhóm đó.
    /// </summary>
    Task<IReadOnlyList<DriveItem>> ListChildrenAsync(
        Guid parentId, Guid? trashGroup, ChildOrder order, bool descending, int skip, int take, CancellationToken ct);

    /// <summary>Thùng rác: các item người dùng đã xoá (gốc của mỗi nhóm), mới xoá trước.</summary>
    Task<IReadOnlyList<DriveItem>> ListTrashAsync(Guid ownerId, int skip, int take, CancellationToken ct);

    /// <summary>Con cháu chưa bị xoá của một thư mục, cha trước con (tối đa <paramref name="take"/>) — để sao chép.</summary>
    Task<IReadOnlyList<DriveItem>> ListSubtreeAsync(DriveItem folder, int take, CancellationToken ct);

    void Add(DriveItem item);

    /// <summary>Cập nhật đường dẫn của mọi con cháu — chạy cùng transaction với lần lưu kế tiếp.</summary>
    void MoveSubtree(SubtreeMove move);

    /// <summary>Đưa con cháu chưa bị xoá vào nhóm thùng rác — cùng transaction với lần lưu kế tiếp.</summary>
    void TrashSubtree(SubtreeTrash trash);

    /// <summary>Khôi phục các con cháu cùng nhóm (và cập nhật đường dẫn nếu đổi chỗ) — cùng transaction.</summary>
    void RestoreSubtree(SubtreeRestore restore);
}

public interface IFileVersionRepository
{
    Task<FileVersion?> FindAsync(Guid versionId, CancellationToken ct);

    Task<IReadOnlyList<FileVersion>> FindManyAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken ct);

    /// <summary>Toàn bộ lịch sử đã lưu của file (được theo dõi để có thể xoá), version mới nhất trước.</summary>
    Task<IReadOnlyList<FileVersion>> ListAsync(Guid itemId, CancellationToken ct);

    void Remove(FileVersion version);
}

public interface IStarRepository
{
    Task<bool> IsStarredAsync(Guid userId, Guid itemId, CancellationToken ct);

    Task<IReadOnlySet<Guid>> StarredAmongAsync(Guid userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct);

    /// <summary>Gắn sao; đã gắn ⇒ không làm gì.</summary>
    Task AddAsync(Star star, CancellationToken ct);

    Task RemoveAsync(Guid userId, Guid itemId, CancellationToken ct);

    /// <summary>Item đã gắn sao và chưa bị xoá, gắn gần đây trước.</summary>
    Task<IReadOnlyList<DriveItem>> ListStarredItemsAsync(Guid userId, int skip, int take, CancellationToken ct);
}

public interface IPendingUploadRepository
{
    Task<PendingUpload?> FindAsync(Guid id, CancellationToken ct);

    void Add(PendingUpload upload);
}
