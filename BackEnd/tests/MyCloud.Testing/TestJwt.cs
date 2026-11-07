using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MyCloud.Testing;

/// <summary>
/// Phát hành JWT RS256 giống Identity bằng một khoá chỉ dùng cho test. Factory cấu hình JwtBearer của Drive
/// tin khoá này thay vì tải JWKS từ Identity.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "mycloud-identity";
    public const string Audience = "mycloud-api";

    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "attacker-key" };

    public static string CreateToken(Guid userId, bool signWithUnknownKey = false, DateTimeOffset? expires = null, bool admin = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["email"] = $"{userId:N}@test.local",
            ["name"] = "Test User",
        };
        if (admin)
        {
            claims["role"] = "admin";
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now.AddMinutes(-1).UtcDateTime,
            NotBefore = now.AddMinutes(-1).UtcDateTime,
            Expires = (expires ?? now.AddMinutes(15)).UtcDateTime,
            SigningCredentials = new SigningCredentials(signWithUnknownKey ? OtherKey : Key, SecurityAlgorithms.RsaSha256),
            Claims = claims,
        });
    }

    /// <summary>Token của một service (client credentials) như Identity phát hành — ADR-014.</summary>
    public static string CreateServiceToken(string clientId = "drive")
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now.AddMinutes(-1).UtcDateTime,
            NotBefore = now.AddMinutes(-1).UtcDateTime,
            Expires = now.AddMinutes(30).UtcDateTime,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "svc:" + clientId,
                ["client_id"] = clientId,
                ["scope"] = "mycloud.internal",
            },
        });
    }

    public static HttpClient AsUser(this HttpClient client, Guid userId, bool admin = false)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId, admin: admin));
        return client;
    }
}
