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
}
