using System.Text.Json.Serialization;
using Carter;
using FluentValidation;
using Identity.Api.Data;
using Identity.Api.Tokens;
using Microsoft.Extensions.Options;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;

namespace Identity.Api.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

/// <param name="RefreshTokenInBody">
/// Client không phải trình duyệt (mobile, CLI) đặt <c>true</c> để nhận refresh token trong body.
/// Mặc định refresh token nằm trong cookie HttpOnly — JavaScript không đọc được.
/// </param>
public sealed record LoginRequest(string Email, string Password, bool RefreshTokenInBody = false);

/// <param name="RefreshToken">Chỉ dùng cho client không phải trình duyệt; trình duyệt gửi <c>{}</c> và dùng cookie.</param>
public sealed record RefreshRequest(string? RefreshToken);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken);

public sealed class AuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("Auth")
            .MapGroup("/api/v{version:apiVersion}/auth")
            .HasApiVersion(1)
            .AllowAnonymous(); // rate limit chặt ở Gateway

        group.MapPost("/register", RegisterAsync).WithName("Register");
        group.MapPost("/login", LoginAsync).WithName("Login");
        group.MapPost("/refresh", RefreshAsync).WithName("Refresh");
        group.MapPost("/logout", LogoutAsync).WithName("Logout");
    }

    internal static ClientInfo GetClientInfo(HttpContext context)
    {
        // Chỉ để ghi nhận phiên (hiển thị/kiểm tra), không dùng cho quyết định bảo mật nên đọc X-Forwarded-For của Gateway.
        string? forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
        string? ip = string.IsNullOrEmpty(forwardedFor) ? context.Connection.RemoteIpAddress?.ToString() : forwardedFor;
        return new ClientInfo(ip, context.Request.Headers.UserAgent.ToString());
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        AccountService accounts,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        Result<Guid> result = await accounts.RegisterAsync(request, ct);
        if (result.IsFailure)
        {
            return result.Error == IdentityErrors.EmailTaken
                ? Problem(StatusCodes.Status409Conflict, "Conflict", result.Error)
                : result.ToProblem();
        }

        return Results.Created($"/api/v1/users/{result.Value}", new { id = result.Value });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        AccountService accounts,
        RefreshTokenCookie cookie,
        HttpContext context,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        Result<TokenPair> result = await accounts.LoginAsync(request, GetClientInfo(context), ct);
        return result.IsSuccess
            ? ToTokenResponse(result.Value, context, cookie, request.RefreshTokenInBody)
            : Problem(StatusCodes.Status401Unauthorized, "Unauthorized", result.Error);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        AccountService accounts,
        RefreshTokenCookie cookie,
        HttpContext context,
        CancellationToken ct)
    {
        // Gửi token trong body ⇒ nhận lại trong body; không thì dùng cookie.
        bool inBody = !string.IsNullOrEmpty(request.RefreshToken);
        string? presented = inBody ? request.RefreshToken : cookie.Read(context.Request);
        if (presented is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "Unauthorized", IdentityErrors.InvalidRefreshToken);
        }

        Result<TokenPair> result = await accounts.RefreshAsync(presented, GetClientInfo(context), ct);
        if (result.IsFailure)
        {
            if (!inBody)
            {
                cookie.Delete(context.Response);
            }

            return Problem(StatusCodes.Status401Unauthorized, "Unauthorized", result.Error);
        }

        return ToTokenResponse(result.Value, context, cookie, inBody);
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest request,
        AccountService accounts,
        RefreshTokenCookie cookie,
        HttpContext context,
        CancellationToken ct)
    {
        string? presented = string.IsNullOrEmpty(request.RefreshToken) ? cookie.Read(context.Request) : request.RefreshToken;
        await accounts.LogoutAsync(presented, ct);
        cookie.Delete(context.Response);
        return Results.NoContent(); // luôn 204: đăng xuất là idempotent
    }

    private static IResult ToTokenResponse(TokenPair tokens, HttpContext context, RefreshTokenCookie cookie, bool refreshTokenInBody)
    {
        if (!refreshTokenInBody)
        {
            cookie.Append(context.Response, tokens.RefreshToken);
        }

        context.Response.Headers.CacheControl = "no-store";
        int expiresIn = (int)Math.Max(0, (tokens.AccessToken.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return Results.Ok(new TokenResponse(
            tokens.AccessToken.Token,
            "Bearer",
            expiresIn,
            refreshTokenInBody ? tokens.RefreshToken.Token : null));
    }

    internal static IResult Problem(int status, string title, Error error) =>
        Results.Problem(title: title, detail: error.Message, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}

/// <summary>Cookie chứa refresh token: HttpOnly, SameSite=Strict, chỉ gửi tới đường dẫn auth.</summary>
public sealed class RefreshTokenCookie(IOptions<TokenOptions> options, IHostEnvironment environment)
{
    private readonly TokenOptions _options = options.Value;

    public string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(_options.CookieName, out string? value) && !string.IsNullOrEmpty(value) ? value : null;

    public void Append(HttpResponse response, IssuedRefreshToken token) =>
        response.Cookies.Append(_options.CookieName, token.Token, Options(token.ExpiresAt));

    public void Delete(HttpResponse response) =>
        response.Cookies.Delete(_options.CookieName, Options(expires: null));

    private CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = _options.CookieSecure ?? !environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict, // chặn CSRF: trình duyệt không gửi cookie trong request từ site khác
        Path = _options.CookiePath,
        Expires = expires,
        IsEssential = true,
    };
}

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(AppUser.DisplayNameMaxLength);
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
