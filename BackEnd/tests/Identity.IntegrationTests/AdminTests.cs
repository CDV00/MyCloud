using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Identity.IntegrationTests;

// Trang quản trị người dùng: quản trị viên từ cấu hình, claim role, khoá tài khoản, không tự khoá mình.
public sealed partial class AuthTests
{
    private sealed record AdminUser(Guid Id, string Email, string DisplayName, bool IsAdmin, bool Disabled, DateTimeOffset? LastLoginAt);

    private sealed record AdminUserPage(AdminUser[] Items, int Total, int Page, int PageSize);

    private sealed record Me(Guid Id, string Email, bool IsAdmin);

    [Fact]
    public async Task ConfiguredAdminEmail_GetsAdminRoleClaim_AndOthersAreForbidden()
    {
        using HttpClient admin = await AdminClientAsync();
        using HttpClient user = factory.CreateClient();
        string email = await RegisterAsync(user);
        TokenResponse token = await LoginAsync(user, email);
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        new JsonWebToken(token.AccessToken).TryGetPayloadValue("role", out string? role).ShouldBeFalse(role);
        (await user.GetFromJsonAsync<Me>("/api/v1/users/me"))!.IsAdmin.ShouldBeFalse();
        (await user.GetAsync("/api/v1/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await admin.GetFromJsonAsync<Me>("/api/v1/users/me"))!.IsAdmin.ShouldBeTrue();
        AdminUserPage page = (await admin.GetFromJsonAsync<AdminUserPage>($"/api/v1/admin/users?q={Uri.EscapeDataString(email[..12])}"))!;
        AdminUser listed = page.Items.ShouldHaveSingleItem();
        listed.Email.ShouldBe(email);
        listed.LastLoginAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DisablingAUser_BlocksLoginAndRefresh_AndEnablingRestoresAccess()
    {
        using HttpClient admin = await AdminClientAsync();
        using HttpClient user = factory.CreateClient();
        string email = await RegisterAsync(user);
        TokenResponse session = await LoginAsync(user, email, refreshTokenInBody: true);
        Guid id = (await admin.GetFromJsonAsync<AdminUserPage>($"/api/v1/admin/users?q={Uri.EscapeDataString(email)}"))!.Items.Single().Id;

        HttpResponseMessage disabled = await admin.PatchAsJsonAsync($"/api/v1/admin/users/{id}", new { disabled = true });
        (await disabled.Content.ReadFromJsonAsync<AdminUser>())!.Disabled.ShouldBeTrue();

        HttpResponseMessage login = await user.PostAsJsonAsync($"{Auth}/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await login.Content.ReadAsStringAsync()).ShouldContain("IDENTITY.ACCOUNT_DISABLED");
        (await user.PostAsJsonAsync($"{Auth}/refresh", new { refreshToken = session.RefreshToken })).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized); // mọi phiên đã bị thu hồi

        await admin.PatchAsJsonAsync($"/api/v1/admin/users/{id}", new { disabled = false });
        (await LoginAsync(user, email)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Admins_CannotDisableOrDemoteThemselves_ButCanPromoteOthers()
    {
        using HttpClient admin = await AdminClientAsync();
        Guid self = (await admin.GetFromJsonAsync<Me>("/api/v1/users/me"))!.Id;
        (await admin.PatchAsJsonAsync($"/api/v1/admin/users/{self}", new { disabled = true })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using HttpClient other = factory.CreateClient();
        string email = await RegisterAsync(other);
        Guid id = (await admin.GetFromJsonAsync<AdminUserPage>($"/api/v1/admin/users?q={Uri.EscapeDataString(email)}"))!.Items.Single().Id;
        await admin.PatchAsJsonAsync($"/api/v1/admin/users/{id}", new { isAdmin = true });

        TokenResponse promoted = await LoginAsync(other, email);
        new JsonWebToken(promoted.AccessToken).GetPayloadValue<string>("role").ShouldBe("admin");
        (await admin.GetFromJsonAsync<AdminUserPage>("/api/v1/admin/users?status=admin"))!.Items.ShouldContain(u => u.Id == id);
    }

    /// <summary>Đăng nhập bằng email quản trị viên trong cấu hình (đăng ký lần đầu nếu chưa có).</summary>
    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync($"{Auth}/register", new { email = IdentityApiFactory.AdminEmail, password = Password, displayName = "Admin" });
        TokenResponse token = await LoginAsync(client, IdentityApiFactory.AdminEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}
