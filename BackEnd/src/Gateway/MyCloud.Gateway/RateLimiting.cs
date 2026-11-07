using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MyCloud.Gateway;

/// <summary>Section <c>RateLimiting</c>. Giá trị mặc định theo docs/03 §1.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Đăng nhập / đăng ký: số request mỗi phút mỗi IP (chống dò mật khẩu).</summary>
    [Range(1, 10_000)]
    public int LoginPermitsPerMinute { get; set; } = 5;

    /// <summary>Refresh / logout: số request mỗi phút mỗi IP.</summary>
    [Range(1, 10_000)]
    public int AuthPermitsPerMinute { get; set; } = 30;

    /// <summary>API thường: số request mỗi giây mỗi user (token bucket, cho phép burst gấp đôi).</summary>
    [Range(1, 100_000)]
    public int ApiPermitsPerSecond { get; set; } = 100;
}

/// <summary>
/// Tên policy dùng trong <c>ReverseProxy:Routes:*:RateLimiterPolicy</c>. Bộ đếm nằm trong bộ nhớ của từng instance;
/// chạy nhiều instance Gateway thì chuyển sang Redis (Phase 6).
/// </summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Auth = "auth";
    public const string Api = "api";

    public static IHostApplicationBuilder AddGatewayRateLimiting(this IHostApplicationBuilder builder)
    {
        RateLimitingOptions limits = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new();
        Validator.ValidateObject(limits, new ValidationContext(limits), validateAllProperties: true);

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                        title: "Too Many Requests",
                        detail: "Too many requests. Please try again later.",
                        statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(context.HttpContext);
            };

            o.AddPolicy(Login, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.LoginPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

            o.AddPolicy(Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.AuthPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

            // Theo user đã xác thực; request chưa xác thực (sẽ bị 401) tính theo IP.
            o.AddPolicy(Api, ctx => RateLimitPartition.GetTokenBucketLimiter(
                ctx.User.FindFirstValue("sub") is { } sub ? $"user:{sub}" : $"ip:{ClientIp(ctx)}",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.ApiPermitsPerSecond * 2,
                    TokensPerPeriod = limits.ApiPermitsPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                }));
        });

        return builder;
    }

    // Sau load balancer cần cấu hình ForwardedHeaders với KnownProxies (Phase 6), nếu không mọi client chung một IP.
    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
