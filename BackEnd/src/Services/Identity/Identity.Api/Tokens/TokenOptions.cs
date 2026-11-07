using System.ComponentModel.DataAnnotations;

namespace Identity.Api.Tokens;

/// <summary>Thời hạn token và cookie chứa refresh token, section <c>Tokens</c>.</summary>
public sealed class TokenOptions
{
    public const string SectionName = "Tokens";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Trong khoảng này sau khi bị xoay vòng, refresh token cũ vẫn được chấp nhận (cấp token mới cùng family) thay vì
    /// bị coi là đánh cắp: hai tab gọi refresh cùng lúc, hoặc response bị mất trên đường về.
    /// </summary>
    public TimeSpan RefreshReuseLeeway { get; set; } = TimeSpan.FromSeconds(10);

    [Required]
    public string CookieName { get; set; } = "mycloud_rt";

    /// <summary>
    /// Đường dẫn mà TRÌNH DUYỆT thấy (qua Gateway): cookie chỉ được gửi kèm request refresh/logout,
    /// không bị gửi theo mọi request API.
    /// </summary>
    [Required]
    public string CookiePath { get; set; } = "/api/identity/v1/auth";

    /// <summary>Mặc định: Secure ở mọi môi trường trừ Development (http://localhost).</summary>
    public bool? CookieSecure { get; set; }

    /// <summary>Xoá refresh token đã hết hạn quá khoảng này.</summary>
    public TimeSpan ExpiredTokenRetention { get; set; } = TimeSpan.FromDays(7);
}
