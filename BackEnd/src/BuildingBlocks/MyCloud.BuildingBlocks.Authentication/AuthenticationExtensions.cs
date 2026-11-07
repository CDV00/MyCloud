using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MyCloud.BuildingBlocks.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>Tên HttpClient dùng để tải discovery document + JWKS (có service discovery và resilience).</summary>
    public const string BackchannelClientName = "jwt-metadata";

    /// <summary>
    /// JWT bearer (RS256, key lấy từ JWKS của Identity) + <b>fallback policy yêu cầu đăng nhập</b>:
    /// mọi endpoint mặc định trả 401 nếu không có token hợp lệ; endpoint công khai phải gọi <c>AllowAnonymous()</c>.
    /// </summary>
    public static IHostApplicationBuilder AddJwtAuthentication(this IHostApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;

        services.AddOptions<JwtSettings>()
            .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(BackchannelClientName);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>, IHttpClientFactory, IHostEnvironment>((options, settings, httpClients, environment) =>
            {
                JwtSettings jwt = settings.Value;

                if (!string.IsNullOrWhiteSpace(jwt.MetadataAddress))
                {
                    options.MetadataAddress = jwt.MetadataAddress;
                    options.RequireHttpsMetadata = jwt.RequireHttpsMetadata ?? !environment.IsDevelopment();
                    options.Backchannel = httpClients.CreateClient(BackchannelClientName);
                    options.AutomaticRefreshInterval = jwt.MetadataRefreshInterval;
                    options.RefreshInterval = TimeSpan.FromMinutes(1); // tối thiểu giữa 2 lần tải lại khi gặp kid lạ
                }

                options.MapInboundClaims = false; // giữ nguyên tên claim: sub, email, name

                // WebSocket của trình duyệt không gắn được header Authorization ⇒ SignalR gửi token qua query
                // "access_token". Chỉ chấp nhận cách này cho đường dẫn hub (docs/03 §7), không cho API thường.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        string? token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.Request.Path.Value?.Contains("/hubs/", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(ServiceIdentity.Policy, ServiceIdentity.BuildPolicy())
            .AddPolicy(AdminRole.Policy, AdminRole.BuildPolicy());
        services.TryAddSingleton(TimeProvider.System);

        return builder;
    }
}
