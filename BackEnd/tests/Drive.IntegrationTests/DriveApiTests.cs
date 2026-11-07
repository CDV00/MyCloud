using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Drive;

namespace Drive.IntegrationTests;

public sealed class DriveApiTests(DriveApiFactory factory) : IClassFixture<DriveApiFactory>
{
    private sealed record CreatedResponse(Guid Id);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly HttpClient _client = factory.CreateClient();

    private HttpClient Client => _client.DefaultRequestHeaders.Authorization is null ? _client.AsUser(_userId) : _client;

    private ITestHarness Harness => factory.Services.GetRequiredService<ITestHarness>();

    [Fact]
    public async Task CreateFolder_Returns201_AndPublishesFolderCreatedThroughOutbox()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/folders", new { name = "Tài liệu", description = "Thư mục test" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        (await Harness.Published.Any<FolderCreated>(x => x.Context.Message.FolderId == id && x.Context.Message.OwnerId == _userId))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Request_WithoutToken_Returns401()
    {
        using HttpClient anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/folders", new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithTokenSignedByUnknownKey_Returns401()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateToken(_userId, signWithUnknownKey: true));

        var response = await client.PostAsJsonAsync("/api/v1/folders", new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithExpiredToken_Returns401()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateToken(_userId, expires: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var response = await client.PostAsJsonAsync("/api/v1/folders", new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OtherUser_CannotTouchFolder_Returns404()
    {
        // IDOR: user B biết id thư mục của A nhưng không được đổi tên, xoá, tạo con hay upload vào đó.
        Guid folderOfA = await CreateFolderAsync("của A");
        using HttpClient userB = factory.CreateClient().AsUser(Guid.NewGuid());

        (await userB.PutAsJsonAsync($"/api/v1/folders/{folderOfA}", new { name = "hacked" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await userB.DeleteAsync($"/api/v1/folders/{folderOfA}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await userB.PostAsJsonAsync("/api/v1/folders", new { name = "con", parentId = folderOfA })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("x")), "file", "x.txt");
        (await userB.PostAsync($"/api/v1/folders/{folderOfA}/files", form)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Thư mục của A không bị ảnh hưởng.
        (await Client.PutAsJsonAsync($"/api/v1/folders/{folderOfA}", new { name = "vẫn của A" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OtherUser_CannotTouchFile_Returns404()
    {
        Guid folder = await CreateFolderAsync("files của A");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("bí mật")), "file", "secret.txt");
        var upload = await Client.PostAsync($"/api/v1/folders/{folder}/files", form);
        Guid fileId = (await upload.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        using HttpClient userB = factory.CreateClient().AsUser(Guid.NewGuid());
        Guid folderOfB = (await (await userB.PostAsJsonAsync("/api/v1/folders", new { name = "của B" }))
            .Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        (await userB.PutAsJsonAsync($"/api/v1/files/{fileId}", new { name = "x.txt", folderId = folderOfB }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await userB.DeleteAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A cũng không chuyển được file của mình vào thư mục của B.
        (await Client.PutAsJsonAsync($"/api/v1/files/{fileId}", new { name = "x.txt", folderId = folderOfB }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateFolder_EmptyName_Returns400ValidationProblem()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/folders", new { name = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Validation Error");
    }

    [Fact]
    public async Task CreateFolder_UnknownParent_Returns404()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/folders", new { name = "x", parentId = Guid.NewGuid() });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateFolder_Unknown_Returns404()
    {
        // Bản cũ trả 500 (NullReferenceException) với id không tồn tại.
        var response = await Client.PutAsJsonAsync($"/api/v1/folders/{Guid.NewGuid()}", new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteFolder_WithChildren_Returns409()
    {
        Guid parent = await CreateFolderAsync("cha");
        await CreateFolderAsync("con", parent);

        var response = await Client.DeleteAsync($"/api/v1/folders/{parent}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UploadFile_ThenUpdateMetadata_KeepsContent_ThenDelete_RemovesContent()
    {
        Guid folder = await CreateFolderAsync("uploads");
        int filesBefore = CountStoredFiles();

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello MyCloud"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", "hello.txt");

        var upload = await Client.PostAsync($"/api/v1/folders/{folder}/files", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid fileId = (await upload.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
        CountStoredFiles().ShouldBe(filesBefore + 1);

        var update = await Client.PutAsJsonAsync($"/api/v1/files/{fileId}", new { name = "renamed.txt", folderId = folder });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Harness.Published.Any<FileUpdated>(x =>
            x.Context.Message.FileId == fileId && x.Context.Message.Name == "renamed.txt" && x.Context.Message.SizeBytes == 13))
            .ShouldBeTrue();

        var delete = await Client.DeleteAsync($"/api/v1/files/{fileId}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        CountStoredFiles().ShouldBe(filesBefore);
        (await Harness.Published.Any<FileDeleted>(x => x.Context.Message.FileId == fileId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Health_Ready_IsAnonymous_AndHealthy()
    {
        using HttpClient anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateFolderAsync(string name, Guid? parentId = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/folders", new { name, parentId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private int CountStoredFiles() =>
        Directory.Exists(factory.ContentRoot) ? Directory.GetFiles(factory.ContentRoot).Length : 0;
}
