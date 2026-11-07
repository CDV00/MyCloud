namespace Identity.Api.Keys;

/// <summary>
/// Khoá RSA dùng để ký access token. Private key được mã hoá bằng ASP.NET Core Data Protection trước khi lưu DB;
/// production cần bảo vệ key ring của Data Protection bằng certificate / Key Vault (docs/06).
/// </summary>
public sealed class SigningKey
{
    public const string Rs256 = "RS256";

    /// <summary>Giá trị <c>kid</c> trong header của JWT và trong JWKS.</summary>
    public string Id { get; set; } = string.Empty;

    public string Algorithm { get; set; } = Rs256;

    /// <summary>PKCS#8 private key, đã được Data Protection mã hoá, dạng base64.</summary>
    public string ProtectedPrivateKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Thu hồi khẩn cấp (lộ key): bỏ khỏi JWKS ngay, mọi token ký bằng nó hết hiệu lực.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
