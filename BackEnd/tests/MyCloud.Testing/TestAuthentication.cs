using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace MyCloud.Testing;

public static class TestAuthentication
{
    /// <summary>
    /// Service dưới test tin khoá của <see cref="TestJwt"/> thay vì tải JWKS từ Identity (không chạy trong test).
    /// Configure (đăng ký sau Program) chạy trước PostConfigure của JwtBearer ⇒ không tạo ConfigurationManager.
    /// </summary>
    public static IServiceCollection TrustTestJwt(this IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.MetadataAddress = null!;
            o.TokenValidationParameters.IssuerSigningKey = TestJwt.Key;
        });
}
