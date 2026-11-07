using System.ComponentModel.DataAnnotations;

namespace MyCloud.BuildingBlocks.Authentication;

/// <summary>Cấu hình validate access token, section <c>Authentication:Jwt</c>.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Authentication:Jwt";

    /// <summary>Giá trị claim <c>iss</c> mà Identity phát hành.</summary>
    [Required]
    public string Issuer { get; set; } = "mycloud-identity";

    /// <summary>Giá trị claim <c>aud</c>.</summary>
    [Required]
    public string Audience { get; set; } = "mycloud-api";

    /// <summary>
    /// Discovery document của Identity (chứa <c>jwks_uri</c>). Tên host được phân giải bằng service discovery
    /// (Aspire / DNS Kubernetes). Để trống thì không tải key từ xa — Identity tự cung cấp key của nó.
    /// </summary>
    public string? MetadataAddress { get; set; } = "http://identity/.well-known/openid-configuration";

    /// <summary>Mặc định chỉ chấp nhận HTTPS ngoài môi trường Development.</summary>
    public bool? RequireHttpsMetadata { get; set; }

    /// <summary>
    /// Chu kỳ tải lại JWKS. Phải nhỏ hơn thời gian Identity công bố key mới trước khi dùng nó để ký
    /// (<c>SigningKeys:ActivationDelay</c>, mặc định 2 giờ) — xem docs/06 §Khoá ký.
    /// </summary>
    public TimeSpan MetadataRefreshInterval { get; set; } = TimeSpan.FromHours(1);
}
