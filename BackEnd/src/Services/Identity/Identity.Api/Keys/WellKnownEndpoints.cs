using Carter;
using Microsoft.Extensions.Options;
using MyCloud.BuildingBlocks.Authentication;

namespace Identity.Api.Keys;

/// <summary>
/// Discovery document tối thiểu (theo OpenID Connect Discovery) + JWKS để Gateway/service tự validate token.
/// Không đi qua Gateway — chỉ dùng trong mạng nội bộ (public key, không phải bí mật).
/// </summary>
public sealed class WellKnownEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/openid-configuration", (HttpRequest request, IOptions<JwtSettings> jwt) =>
            {
                // jwks_uri dựng từ chính request: đúng cả khi gọi qua service discovery (localhost:port) lẫn DNS nội bộ.
                string baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
                return Results.Json(new Dictionary<string, object>
                {
                    ["issuer"] = jwt.Value.Issuer,
                    ["jwks_uri"] = $"{baseUrl}/.well-known/jwks.json",
                    ["id_token_signing_alg_values_supported"] = new[] { SigningKey.Rs256 },
                });
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        app.MapGet("/.well-known/jwks.json", (HttpResponse response, SigningKeyStore keys) =>
            {
                response.Headers.CacheControl = "public, max-age=300";
                return Results.Json(keys.Current.Jwks);
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
    }
}
