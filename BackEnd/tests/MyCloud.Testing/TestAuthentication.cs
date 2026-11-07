using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace MyCloud.Testing;

public static class TestAuthentication
{
    /// <summary>Service dưới test lấy token service ký bằng khoá test thay vì gọi Identity.</summary>
    public static IServiceCollection UseTestServiceToken(this IServiceCollection services, string clientId = "drive") =>
        services.AddSingleton<MyCloud.BuildingBlocks.Authentication.IServiceTokenProvider>(new TestServiceTokenProvider(clientId));

    private sealed class TestServiceTokenProvider(string clientId) : MyCloud.BuildingBlocks.Authentication.IServiceTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken ct) => Task.FromResult(TestJwt.CreateServiceToken(clientId));
    }

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
