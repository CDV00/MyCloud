namespace Identity.Api.Tokens;

/// <summary>
/// Refresh token đã phát hành. Chỉ lưu hash SHA-256 — lộ DB không lộ token.
/// Mỗi lần đăng nhập mở một <see cref="FamilyId"/>; mỗi lần refresh thay token cũ bằng token mới cùng family (rotation).
/// Token cũ bị dùng lại ⇒ thu hồi cả family (dấu hiệu token bị đánh cắp).
/// </summary>
public sealed class RefreshToken
{
    public const int TokenHashLength = 64;

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    /// <summary>SHA-256 của token, dạng hex chữ thường.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    /// <summary>Token thay thế khi bị xoay vòng.</summary>
    public Guid? ReplacedById { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }
}

public static class RevocationReasons
{
    public const string Rotated = "rotated";
    public const string Logout = "logout";
    public const string ReuseDetected = "reuse-detected";
    public const string PasswordChanged = "password-changed";
    public const string UserUnavailable = "user-unavailable";
}
