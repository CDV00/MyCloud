using Drive.Domain;
using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace Drive.Application.Abstractions;

/// <summary>Vai trò trên một item, tăng dần (docs/03 §5). Commenter hiện có quyền như Viewer (chưa có bình luận).</summary>
public enum AccessRole
{
    None = 0,
    Viewer = 1,
    Commenter = 2,
    Editor = 3,
    Owner = 4,
}

/// <summary>Loại thao tác ⇒ vai trò tối thiểu.</summary>
public enum ItemPermission
{
    /// <summary>Xem, tải, liệt kê, xem version — Viewer.</summary>
    View,

    /// <summary>Đổi tên, di chuyển, tạo / tải lên bên trong, version — Editor.</summary>
    Edit,

    /// <summary>Chia sẻ, tạo link — Editor (docs/03 §5 <c>can_share: owner or editor</c>).</summary>
    Share,

    /// <summary>Thùng rác, khôi phục, xoá vĩnh viễn — chỉ chủ sở hữu (<c>can_delete: owner</c>).</summary>
    Manage,
}

/// <summary>Ai đang truy cập: người dùng đã đăng nhập và/hoặc người mở link chia sẻ (có thể ẩn danh).</summary>
public sealed record AccessPrincipal(Guid? UserId, string? LinkToken, string? LinkPassword);

/// <summary>Token + mật khẩu link của request hiện tại — endpoint công khai <c>/public/links/{token}</c> đặt vào.</summary>
public sealed class LinkContext
{
    public string? Token { get; set; }

    public string? Password { get; set; }
}

/// <summary>
/// Vai trò của người không phải chủ sở hữu (chủ sở hữu được ItemAccess xử lý ngay, không gọi đi — fast-path ADR-007).
/// Triển khai: Sharing service qua gRPC, gửi kèm chủ sở hữu + tổ tiên (ADR-014).
/// </summary>
public interface IItemAuthorizer
{
    Task<IReadOnlyList<AccessRole>> GetRolesAsync(AccessPrincipal principal, IReadOnlyList<DriveItem> items, CancellationToken ct);
}

/// <summary>Chỉ chủ sở hữu (không chia sẻ) — dùng khi không có Sharing service.</summary>
public sealed class OwnerOnlyItemAuthorizer : IItemAuthorizer
{
    public Task<IReadOnlyList<AccessRole>> GetRolesAsync(AccessPrincipal principal, IReadOnlyList<DriveItem> items, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AccessRole>>(items.Select(_ => AccessRole.None).ToArray());
}

/// <summary>
/// Nạp item + kiểm tra quyền.
/// <list type="bullet">
/// <item>Không có quyền gì ⇒ 404 như không tồn tại (không lộ id hợp lệ).</item>
/// <item>Thấy được nhưng không đủ quyền ⇒ 403 <c>DRIVE.ACCESS_DENIED</c> (vd người xem muốn đổi tên).</item>
/// <item>Item trong thùng rác chỉ chủ sở hữu thấy; xem được, không sửa được (409 <c>DRIVE.ITEM_TRASHED</c>) trừ các
/// thao tác của chính thùng rác (<see cref="GetIncludingTrashedAsync"/>).</item>
/// </list>
/// Vai trò đã tính được nhớ trong request để dựng DTO không phải hỏi lại.
/// </summary>
public sealed class ItemAccess(IDriveItemRepository items, IItemAuthorizer authorizer, ICurrentUser currentUser, LinkContext link)
{
    private readonly Dictionary<Guid, AccessRole> _roles = [];

    public Guid UserId => currentUser.GetRequiredUserId();

    public Guid? OptionalUserId => currentUser.UserId;

    public AccessPrincipal Principal => new(currentUser.UserId, link.Token, link.Password);

    public static AccessRole Required(ItemPermission permission) => permission switch
    {
        ItemPermission.View => AccessRole.Viewer,
        ItemPermission.Manage => AccessRole.Owner,
        _ => AccessRole.Editor,
    };

    /// <summary>Quyền xem chấp nhận item trong thùng rác (chỉ chủ sở hữu thấy nó); quyền khác thì không.</summary>
    public async Task<DriveItem> GetAsync(Guid itemId, ItemPermission permission, CancellationToken ct)
    {
        DriveItem item = await GetIncludingTrashedAsync(itemId, permission, ct);
        return permission != ItemPermission.View && item.IsTrashed ? throw new DriveException.ItemTrashed(itemId) : item;
    }

    public async Task<DriveItem> GetIncludingTrashedAsync(Guid itemId, ItemPermission permission, CancellationToken ct)
    {
        DriveItem? item = await items.FindAsync(itemId, ct);
        if (item is null)
        {
            throw new DriveException.ItemNotFound(itemId);
        }

        AccessRole role = (await RolesAsync([item], ct))[0];
        if (role == AccessRole.None || (item.IsTrashed && role != AccessRole.Owner))
        {
            throw new DriveException.ItemNotFound(itemId);
        }

        return role >= Required(permission) ? item : throw new DriveException.AccessDenied(itemId);
    }

    /// <summary>Item phải còn hoạt động kể cả khi chỉ cần quyền xem — vd nguồn để sao chép, gắn sao.</summary>
    public async Task<DriveItem> GetActiveAsync(Guid itemId, ItemPermission permission, CancellationToken ct)
    {
        DriveItem item = await GetIncludingTrashedAsync(itemId, permission, ct);
        return item.IsTrashed ? throw new DriveException.ItemTrashed(itemId) : item;
    }

    public async Task<DriveItem> GetFolderAsync(Guid itemId, ItemPermission permission, CancellationToken ct)
    {
        DriveItem item = await GetAsync(itemId, permission, ct);
        return item.IsFolder ? item : throw new DriveException.NotAFolder(itemId);
    }

    public async Task<DriveItem> GetFileAsync(Guid itemId, ItemPermission permission, CancellationToken ct)
    {
        DriveItem item = await GetAsync(itemId, permission, ct);
        return item.IsFolder ? throw new DriveException.NotAFile(itemId) : item;
    }

    /// <summary>Vai trò trên nhiều item: chủ sở hữu tính ngay, còn lại một lời gọi Sharing cho cả lô.</summary>
    public async Task<IReadOnlyList<AccessRole>> RolesAsync(IReadOnlyList<DriveItem> list, CancellationToken ct)
    {
        Guid? userId = currentUser.UserId;
        DriveItem[] unknown = list
            .Where(i => !_roles.ContainsKey(i.Id) && i.OwnerId != userId)
            .DistinctBy(i => i.Id)
            .ToArray();
        if (unknown.Length > 0)
        {
            IReadOnlyList<AccessRole> fetched = await authorizer.GetRolesAsync(Principal, unknown, ct);
            for (int i = 0; i < unknown.Length; i++)
            {
                _roles[unknown[i].Id] = fetched[i];
            }
        }

        return list.Select(i => i.OwnerId == userId ? AccessRole.Owner : _roles[i.Id]).ToArray();
    }
}
