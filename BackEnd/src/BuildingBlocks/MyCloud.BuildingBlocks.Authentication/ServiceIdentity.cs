using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MyCloud.BuildingBlocks.Authentication;

/// <summary>
/// Định danh của chính các service (OAuth 2.0 client credentials do Identity phát hành — ADR-014). Dùng khi một
/// service hành động thay cho người khác mà JWT của người dùng không đủ: Drive gọi Storage cho chủ sở hữu file khi
/// người được chia sẻ tải về, gọi Sharing khi truy cập ẩn danh qua link. Token mang <c>sub = svc:{clientId}</c>
/// (không phải Guid ⇒ <c>ICurrentUser.UserId</c> luôn null) và scope <see cref="InternalScope"/>.
/// </summary>
public static class ServiceIdentity
{
    public const string ClientIdClaim = "client_id";
    public const string ScopeClaim = "scope";
    public const string InternalScope = "mycloud.internal";
    public const string SubjectPrefix = "svc:";

    /// <summary>Policy cho endpoint chỉ service được gọi (gRPC nội bộ).</summary>
    public const string Policy = "service";

    public static bool IsService(this ClaimsPrincipal principal) =>
        principal.FindFirst(ClientIdClaim) is not null
        && principal.FindAll(ScopeClaim).Any(c => c.Value.Split(' ').Contains(InternalScope));

    internal static AuthorizationPolicy BuildPolicy() =>
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(c => c.User.IsService()).Build();

    /// <summary>
    /// Cho service này lấy token của chính nó (section <c>ServiceIdentity</c>: ClientId, ClientSecret, TokenEndpoint)
    /// và gắn vào lời gọi đi qua <see cref="ServiceTokenHandler"/>.
    /// </summary>
    public static IHostApplicationBuilder AddServiceIdentity(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ServiceIdentityOptions>().Bind(builder.Configuration.GetSection(ServiceIdentityOptions.SectionName));
        builder.Services.AddHttpClient(ServiceTokenProvider.HttpClientName);
        builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
        builder.Services.AddTransient<ServiceTokenHandler>();
        return builder;
    }
}

public sealed class ServiceIdentityOptions
{
    public const string SectionName = "ServiceIdentity";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Endpoint token của Identity (nội bộ, không đi qua Gateway); host phân giải bằng service discovery.</summary>
    public string TokenEndpoint { get; set; } = "http://identity/connect/token";
}

public interface IServiceTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
}

/// <summary>Lấy và cache token của service; làm mới trước khi hết hạn 2 phút. An toàn khi gọi đồng thời.</summary>
internal sealed class ServiceTokenProvider(
    IHttpClientFactory httpClients,
    IOptions<ServiceIdentityOptions> options,
    TimeProvider timeProvider) : IServiceTokenProvider, IDisposable
{
    public const string HttpClientName = "service-token";

    private static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && timeProvider.GetUtcNow() < _expiresAt - RefreshBefore)
        {
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is { } fresh && timeProvider.GetUtcNow() < _expiresAt - RefreshBefore)
            {
                return fresh;
            }

            ServiceIdentityOptions o = options.Value;
            if (string.IsNullOrEmpty(o.ClientId) || string.IsNullOrEmpty(o.ClientSecret))
            {
                throw new InvalidOperationException("ServiceIdentity:ClientId and ServiceIdentity:ClientSecret must be configured.");
            }

            using HttpResponseMessage response = await httpClients.CreateClient(HttpClientName).PostAsync(
                o.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = o.ClientId,
                    ["client_secret"] = o.ClientSecret,
                }),
                ct);
            response.EnsureSuccessStatusCode();
            TokenResponse token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Empty token response.");

            _token = token.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Gắn token của service (thay cho token người dùng) vào lời gọi đi.</summary>
public sealed class ServiceTokenHandler(IServiceTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}
