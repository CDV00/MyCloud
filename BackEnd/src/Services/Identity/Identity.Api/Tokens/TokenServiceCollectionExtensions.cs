using Identity.Api.Auth;
using Identity.Api.Data;
using Identity.Api.Keys;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;

namespace Identity.Api.Tokens;

public static class TokenServiceCollectionExtensions
{
    /// <summary>
    /// Khoá ký (RS256, xoay vòng), access token, refresh token. Identity validate token của chính nó bằng khoá
    /// trong bộ nhớ thay vì tự gọi JWKS qua HTTP.
    /// </summary>
    public static IHostApplicationBuilder AddTokenServices(this IHostApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;

        services.AddDataProtection()
            .SetApplicationName("mycloud-identity")
            .PersistKeysToDbContext<AppIdentityDbContext>();

        services.AddOptions<SigningKeyOptions>()
            .Bind(builder.Configuration.GetSection(SigningKeyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<TokenOptions>()
            .Bind(builder.Configuration.GetSection(TokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SigningKeyStore>();
        services.AddHostedService<SigningKeyReloader>();
        services.AddHostedService<ExpiredRefreshTokenCleanup>();

        services.AddSingleton<AccessTokenService>();
        services.AddScoped<RefreshTokenService>();
        services.AddSingleton<RefreshTokenCookie>();
        services.AddScoped<AccountService>();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyStore>((options, keys) =>
            {
                options.MetadataAddress = null!; // không tải JWKS từ xa
                options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => keys.Current.ValidationKeys;
            });

        return builder;
    }
}
