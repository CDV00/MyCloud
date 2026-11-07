using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using MyCloud.Contracts.Identity.V1;

namespace Identity.IntegrationTests;

// Phase 4: định danh service (client credentials, ADR-014) + tra cứu người dùng.
public sealed partial class AuthTests
{
    [Fact]
    public async Task ClientCredentials_IssuesServiceToken_ThatMayCallUserGrpc()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);

        string serviceToken = await ServiceTokenAsync(client, IdentityApiFactory.ServiceClientSecret);
        UserService.UserServiceClient grpc = GrpcClient(serviceToken);
        FindUserByEmailResponse found = await grpc.FindUserByEmailAsync(new FindUserByEmailRequest { Email = email.ToUpperInvariant() });
        FindUserByEmailResponse missing = await grpc.FindUserByEmailAsync(new FindUserByEmailRequest { Email = "nobody@example.com" });

        found.Found.ShouldBeTrue();
        found.User.Email.ShouldBe(email);
        missing.Found.ShouldBeFalse();
        BatchGetUsersResponse batch = await grpc.BatchGetUsersAsync(new BatchGetUsersRequest { Ids = { found.User.Id, Guid.NewGuid().ToString() } });
        batch.Users.ShouldHaveSingleItem().DisplayName.ShouldBe("Test");
    }

    [Fact]
    public async Task ClientCredentials_WrongSecret_Returns401_AndUserTokenCannotCallUserGrpc()
    {
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage wrong = await client.PostAsync("/connect/token", Form("drive", "not-the-secret"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/connect/token", Form("unknown-client", IdentityApiFactory.ServiceClientSecret))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        string email = await RegisterAsync(client);
        TokenResponse user = await LoginAsync(client, email);
        RpcException ex = await Should.ThrowAsync<RpcException>(() =>
            GrpcClient(user.AccessToken).FindUserByEmailAsync(new FindUserByEmailRequest { Email = email }).ResponseAsync);
        ex.StatusCode.ShouldBe(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task BatchGetUsers_ReturnsDisplayNamesForKnownIds()
    {
        using HttpClient client = factory.CreateClient();
        string email = await RegisterAsync(client);
        TokenResponse token = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        ProfileResponse me = (await client.GetFromJsonAsync<ProfileResponse>("/api/v1/users/me"))!;

        JsonElement users = await client.GetFromJsonAsync<JsonElement>($"/api/v1/users?ids={me.Id},{Guid.NewGuid()}");

        users.GetArrayLength().ShouldBe(1);
        users[0].GetProperty("displayName").GetString().ShouldBe(me.DisplayName);
    }

    private static FormUrlEncodedContent Form(string clientId, string secret) => new(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = clientId,
        ["client_secret"] = secret,
    });

    private static async Task<string> ServiceTokenAsync(HttpClient client, string secret)
    {
        HttpResponseMessage response = await client.PostAsync("/connect/token", Form("drive", secret));
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("expires_in").GetInt32().ShouldBeGreaterThan(0);
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private UserService.UserServiceClient GrpcClient(string token) =>
        new(GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler(),
            Credentials = ChannelCredentials.Create(
                ChannelCredentials.Insecure,
                CallCredentials.FromInterceptor((_, metadata) =>
                {
                    metadata.Add("Authorization", $"Bearer {token}");
                    return Task.CompletedTask;
                })),
            UnsafeUseInsecureChannelCallCredentials = true,
        }));
}
