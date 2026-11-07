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

    private readonly HttpClient _client = factory.CreateClient();

    private ITestHarness Harness => factory.Services.GetRequiredService<ITestHarness>();

    [Fact]
    public async Task CreateFolder_Returns201_AndPublishesFolderCreatedThroughOutbox()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/folders", new { name = "Tài liệu", description = "Thư mục test" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid id = (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        (await Harness.Published.Any<FolderCreated>(x => x.Context.Message.FolderId == id)).ShouldBeTrue();
    }

    [Fact]
    public async Task CreateFolder_EmptyName_Returns400ValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/folders", new { name = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Validation Error");
    }

    [Fact]
    public async Task CreateFolder_UnknownParent_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/folders", new { name = "x", parentId = Guid.NewGuid() });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateFolder_Unknown_Returns404()
    {
        // Bản cũ trả 500 (NullReferenceException) với id không tồn tại.
        var response = await _client.PutAsJsonAsync($"/api/v1/folders/{Guid.NewGuid()}", new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteFolder_WithChildren_Returns409()
    {
        Guid parent = await CreateFolderAsync("cha");
        await CreateFolderAsync("con", parent);

        var response = await _client.DeleteAsync($"/api/v1/folders/{parent}");

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

        var upload = await _client.PostAsync($"/api/v1/folders/{folder}/files", form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        Guid fileId = (await upload.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
        CountStoredFiles().ShouldBe(filesBefore + 1);

        var update = await _client.PutAsJsonAsync($"/api/v1/files/{fileId}", new { name = "renamed.txt", folderId = folder });
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Harness.Published.Any<FileUpdated>(x =>
            x.Context.Message.FileId == fileId && x.Context.Message.Name == "renamed.txt" && x.Context.Message.SizeBytes == 13))
            .ShouldBeTrue();

        var delete = await _client.DeleteAsync($"/api/v1/files/{fileId}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        CountStoredFiles().ShouldBe(filesBefore);
        (await Harness.Published.Any<FileDeleted>(x => x.Context.Message.FileId == fileId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Health_Ready_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateFolderAsync(string name, Guid? parentId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/folders", new { name, parentId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private int CountStoredFiles() =>
        Directory.Exists(factory.ContentRoot) ? Directory.GetFiles(factory.ContentRoot).Length : 0;
}
