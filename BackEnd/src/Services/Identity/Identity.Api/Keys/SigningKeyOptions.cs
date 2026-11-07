using System.ComponentModel.DataAnnotations;

namespace Identity.Api.Keys;

/// <summary>
/// Vòng đời khoá ký, section <c>SigningKeys</c>.
/// <para>
/// Khoá mới được tạo khi khoá hiện tại đạt <c>RotationInterval - ActivationDelay</c> tuổi và được công bố trong JWKS ngay,
/// nhưng chỉ bắt đầu dùng để ký sau <see cref="ActivationDelay"/> — đủ để mọi service tải lại JWKS
/// (<c>Authentication:Jwt:MetadataRefreshInterval</c>). Khoá cũ còn trong JWKS thêm <see cref="RetentionAfterRotation"/>
/// để token đã phát hành vẫn validate được tới khi hết hạn.
/// </para>
/// </summary>
public sealed class SigningKeyOptions
{
    public const string SectionName = "SigningKeys";

    public TimeSpan RotationInterval { get; set; } = TimeSpan.FromDays(90);

    public TimeSpan ActivationDelay { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan RetentionAfterRotation { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Chu kỳ đọc lại khoá từ DB (nhiều instance Identity dùng chung bảng signing_keys).</summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromMinutes(5);

    [Range(2048, 4096)]
    public int KeySizeBits { get; set; } = 2048;
}
