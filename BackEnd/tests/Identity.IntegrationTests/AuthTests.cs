using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyCloud.Contracts.Events.Identity;

namespace Identity.IntegrationTests;

public sealed partial class AuthTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Password = "correct-horse-1";
    private const string Auth = IdentityApiFactory.AuthPath;

    private sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string? RefreshToken);

    private sealed record ProfileResponse(Guid Id, string Email, string DisplayName);

    private ITestHarness Harness => factory.Services.GetRequiredService<ITestHarness>();

    [Fact]
    public async Task Register_ThenLogin_IssuesRs256TokenVerifiableWithJwks()
    {
        using HttpClient client = factory.CreateClient();
        string email = NewEmail();

        var register = await client.PostAsJsonAsync($"{Auth}/register", new { email, password = Password, displayName = "Nguyễn Văn A" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Harness.Published.Any<UserRegistered>(x => x.Context.Message.Email == email)).ShouldBeTrue();

        TokenResponse tokens = await LoginAsync(client, email);
        tokens.TokenType.ShouldBe("Bearer");
        tokens.ExpiresIn.ShouldBeInRange(14 * 60, 15 * 60);
        tokens.RefreshToken.ShouldBeNull(); // trình duyệt: refresh token chỉ nằm trong cookie HttpOnly

        // Discovery → JWKS → validate chữ ký giống cách Gateway/service làm.
        using JsonDocument discovery = JsonDocument.Parse(await client.GetStringAsync("/.well-known/openid-configuration"));
        string jwksUri = discovery.RootElement.GetProperty("jwks_uri").GetString()!;
        var jwks = new JsonWebKeySet(await client.GetStringAsync(new Uri(jwksUri).PathAndQuery));

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "mycloud-identity",
            ValidAudience = "mycloud-api",
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Alg.ShouldBe("RS256");
        jwt.Kid.ShouldNotBeNullOrEmpty();
        jwt.GetClaim("email").Value.ShouldBe(email);
        jwt.GetClaim("name").Value.ShouldBe("Nguyễn Văn A");
        Guid.TryParse(jwt.Subject, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Login_SetsHardenedRefreshCookie()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);

        var response = await client.PostAsJsonAsync($"{Auth}/login", new { email, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("mycloud_rt=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain($"path={Auth}", Case.Insensitive);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_DuplicateEmail_IgnoringCase_Returns409()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);

        var response = await client.PostAsJsonAsync($"{Auth}/register", new { email = email.ToUpperInvariant(), password = Password, displayName = "B" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ReturnSameGeneric401()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);

        var wrongPassword = await client.PostAsJsonAsync($"{Auth}/login", new { email, password = "wrong-password" });
        var unknownEmail = await client.PostAsJsonAsync($"{Auth}/login", new { email = NewEmail(), password = Password });

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Detail(wrongPassword)).ShouldBe(await Detail(unknownEmail)); // không lộ email có tồn tại hay không
    }

    [Fact]
    public async Task Login_AfterFiveFailures_IsLockedOut_EvenWithCorrectPassword()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);

        for (int i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync($"{Auth}/login", new { email, password = "wrong-password" });
        }

        var response = await client.PostAsJsonAsync($"{Auth}/login", new { email, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithCookie_RotatesRefreshToken_AndIssuesNewAccessToken()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);
        var login = await client.PostAsJsonAsync($"{Auth}/login", new { email, password = Password });
        string firstCookie = RefreshCookie(login);

        var refresh = await client.PostAsJsonAsync($"{Auth}/refresh", new { });

        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await refresh.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken.ShouldNotBeNullOrEmpty();
        RefreshCookie(refresh).ShouldNotBe(firstCookie);
    }

    [Fact]
    public async Task Refresh_WithoutCookieOrBody_Returns401()
    {
        using HttpClient client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{Auth}/refresh", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_RevokesWholeFamily()
    {
        using HttpClient client = factory.CreateClient(new() { HandleCookies = false });
        string email = await RegisterAsync(client);
        string rt1 = (await LoginAsync(client, email, refreshTokenInBody: true)).RefreshToken!;

        string rt2 = (await RefreshAsync(client, rt1)).RefreshToken!;

        // Kẻ trộm (hoặc client cũ) dùng lại rt1 ⇒ bị từ chối và cả family bị thu hồi, kể cả rt2 hợp lệ.
        (await client.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken = rt1 })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken = rt2 })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Phiên đăng nhập khác của cùng user không bị ảnh hưởng.
        string otherSession = (await LoginAsync(client, email, refreshTokenInBody: true)).RefreshToken!;
        (await client.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken = otherSession })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        using HttpClient client = factory.CreateClient(new() { HandleCookies = false });
        string email = await RegisterAsync(client);
        string rt = (await LoginAsync(client, email, refreshTokenInBody: true)).RefreshToken!;

        (await client.PostAsJsonAsync($"{Auth}/logout", new { refreshToken = rt })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken = rt })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using HttpClient client = factory.CreateClient();

        (await client.GetAsync("/api/v1/users/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ReturnsProfile_AndPatchUpdatesDisplayName()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await LoginAsync(client, email)).AccessToken);

        ProfileResponse me = (await client.GetFromJsonAsync<ProfileResponse>("/api/v1/users/me"))!;
        me.Email.ShouldBe(email);

        var patch = await client.PatchAsJsonAsync("/api/v1/users/me", new { displayName = "Tên mới" });
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await patch.Content.ReadFromJsonAsync<ProfileResponse>())!.DisplayName.ShouldBe("Tên mới");
        (await Harness.Published.Any<UserProfileChanged>(x => x.Context.Message.UserId == me.Id && x.Context.Message.DisplayName == "Tên mới"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ChangePassword_RevokesAllSessions_AndOnlyNewPasswordWorks()
    {
        using HttpClient client = factory.CreateClient(new() { HandleCookies = false });
        string email = await RegisterAsync(client);
        TokenResponse session1 = await LoginAsync(client, email, refreshTokenInBody: true);
        TokenResponse session2 = await LoginAsync(client, email, refreshTokenInBody: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/me/password")
        {
            Content = JsonContent.Create(new { currentPassword = Password, newPassword = "new-password-2" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session1.AccessToken);
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken = session1.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken = session2.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"{Auth}/login", new { email, password = Password })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"{Auth}/login", new { email, password = "new-password-2" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await LoginAsync(client, email)).AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/users/me/password", new { currentPassword = "nope-nope", newPassword = "new-password-2" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("IDENTITY.PASSWORD_MISMATCH");
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        using HttpClient client = factory.CreateClient();

        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        string email = NewEmail();
        var response = await client.PostAsJsonAsync($"{Auth}/register", new { email, password = Password, displayName = "Test" });
        response.EnsureSuccessStatusCode();
        return email;
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string email, bool refreshTokenInBody = false)
    {
        var response = await client.PostAsJsonAsync($"{Auth}/login", new { email, password = Password, refreshTokenInBody });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static async Task<TokenResponse> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("mycloud_rt=", StringComparison.Ordinal)).Split(';')[0];

    private static async Task<string?> Detail(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("detail").GetString();
    }
}
