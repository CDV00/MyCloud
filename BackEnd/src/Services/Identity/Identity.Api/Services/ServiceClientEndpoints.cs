using System.Security.Cryptography;
using System.Text;
using Identity.Api.Keys;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyCloud.BuildingBlocks.Authentication;

namespace Identity.Api.Services;

/// <summary>Các service được phép lấy token của chính mình, section <c>ServiceClients:Clients:{clientId}:Secret</c>.</summary>
public sealed class ServiceClientOptions
{
    public const string SectionName = "ServiceClients";

    public Dictionary<string, ServiceClient> Clients { get; set; } = new(StringComparer.Ordinal);

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    public sealed class ServiceClient
    {
        public string Secret { get; set; } = string.Empty;
    }
}

/// <summary>
/// OAuth 2.0 client credentials cho service-to-service (ADR-014). Endpoint nằm ngoài <c>/api</c> nên Gateway không
/// định tuyến tới — chỉ gọi được trong mạng nội bộ. Token cùng khoá ký RS256 với token người dùng (cùng JWKS), mang
/// <c>sub = svc:{clientId}</c>, <c>client_id</c> và scope nội bộ.
/// </summary>
public static class ServiceClientEndpoints
{
    public static void MapServiceClientEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/connect/token", IssueAsync).AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();

    private static async Task<IResult> IssueAsync(
        HttpRequest request,
        IOptions<ServiceClientOptions> options,
        SigningKeyStore keys,
        IOptions<JwtSettings> jwt,
        TimeProvider timeProvider)
    {
        if (!request.HasFormContentType)
        {
            return Error("invalid_request");
        }

        IFormCollection form = await request.ReadFormAsync();
        if (form["grant_type"] != "client_credentials")
        {
            return Error("unsupported_grant_type");
        }

        string clientId = form["client_id"].ToString();
        string secret = form["client_secret"].ToString();
        if (!options.Value.Clients.TryGetValue(clientId, out ServiceClientOptions.ServiceClient? client)
            || string.IsNullOrEmpty(client.Secret)
            || !CryptographicOperations.FixedTimeEquals(Hash(secret), Hash(client.Secret)))
        {
            return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        TimeSpan lifetime = options.Value.TokenLifetime;
        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Value.Issuer,
            Audience = jwt.Value.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + lifetime).UtcDateTime,
            SigningCredentials = keys.Current.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = ServiceIdentity.SubjectPrefix + clientId,
                [ServiceIdentity.ClientIdClaim] = clientId,
                [ServiceIdentity.ScopeClaim] = ServiceIdentity.InternalScope,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        });

        return Results.Json(new { access_token = token, token_type = "Bearer", expires_in = (int)lifetime.TotalSeconds });
    }

    // So sánh hash để thời gian không phụ thuộc độ dài / nội dung secret.
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static IResult Error(string error) => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
}
