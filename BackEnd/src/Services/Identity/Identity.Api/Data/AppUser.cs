using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Data;

/// <summary>
/// Tài khoản người dùng. Đã bỏ các trường kế thừa từ hệ thống nhân sự cũ
/// (IsDirector, PositionId, ManagerId, ...) vì không thuộc nghiệp vụ Drive.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Quản trị viên hệ thống (trang quản trị người dùng). Token mang claim <c>role = admin</c>.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>Có giá trị ⇒ tài khoản bị quản trị viên khoá: không đăng nhập / refresh được.</summary>
    public DateTimeOffset? DisabledAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}
