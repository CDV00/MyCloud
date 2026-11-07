using Identity.Api.Data;
using Identity.Api.Keys;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using MyCloud.BuildingBlocks.Authentication;

namespace Identity.Api.Tokens;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Phát hành access token JWT RS256 (claim: sub, email, name, jti) — docs/03 §2 Token.</summary>
public sealed class AccessTokenService(
    SigningKeyStore keys,
    IOptions<JwtSettings> jwt,
    IOptions<TokenOptions> tokens,
    TimeProvider timeProvider)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Create(AppUser user)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expires = now + tokens.Value.AccessTokenLifetime;

        string token = Handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = jwt.Value.Issuer,
            Audience = jwt.Value.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = keys.Current.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email ?? string.Empty,
                [JwtRegisteredClaimNames.Name] = user.DisplayName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        });

        return new AccessToken(token, expires);
    }
}
