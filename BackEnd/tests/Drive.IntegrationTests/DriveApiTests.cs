using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Testing;

namespace Drive.IntegrationTests;

public sealed partial class DriveApiTests(DriveApiFactory factory) : IClassFixture<DriveApiFactory>
{
    private sealed record Item(
        Guid Id, Guid? ParentId, string Kind, string Name, string? MimeType, long SizeBytes, bool IsRoot, long Revision,
        bool Starred = false, DateTimeOffset? TrashedAt = null, int VersionNo = 0, Guid OwnerId = default, string Role = "",
        bool HasThumbnail = false);

    private sealed record Page(Item[] Items, string? NextPageToken);

    private sealed record Upload(Guid UploadId, long PartSize, int PartCount, Part[] Parts);

    private sealed record Part(int PartNumber, string Url);

    private sealed record Link(string Url, DateTimeOffset ExpiresAt);

    private ITestHarness Harness => factory.Services.GetRequiredService<ITestHarness>();

    // Mỗi test dùng một người dùng mới ⇒ dữ liệu không lẫn nhau dù chung database.
    private HttpClient NewUser() => factory.CreateClient().AsUser(Guid.NewGuid());

    [Fact]
    public async Task Root_IsCreatedOnce_PerUser()
    {
        using HttpClient client = NewUser();

        Item first = await GetRootAsync(client);
        Item second = await GetRootAsync(client);

        first.IsRoot.ShouldBeTrue();
        first.Kind.ShouldBe("folder");
        second.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task CreateFolder_Returns201WithETag_AndPublishesItemCreated()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/folders", new { parentId = root.Id, name = "Tài liệu" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.ETag!.Tag.ShouldBe("\"1\"");
        Item folder = (await response.Content.ReadFromJsonAsync<Item>())!;
        folder.ParentId.ShouldBe(root.Id);
        (await Harness.Published.Any<ItemCreated>(x =>
            x.Context.Message.ItemId == folder.Id && x.Context.Message.AncestorIds.SequenceEqual(new[] { root.Id }))).ShouldBeTrue();
    }

    [Fact]
    public async Task CreateFolder_DuplicateName_IgnoringCaseAndUnicodeForm_Returns409()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        await CreateFolderAsync(client, root.Id, "Báo cáo");

        var response = await client.PostAsJsonAsync("/api/v1/folders", new { parentId = root.Id, name = "BÁO CÁO" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCode(response)).ShouldBe("DRIVE.NAME_CONFLICT");
    }

    [Fact]
    public async Task ListChildren_FoldersFirst_SortedByName_WithPaging()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        await UploadFileAsync(client, root.Id, "a-file.txt", 10);
        foreach (string name in new[] { "Charlie", "alpha", "Bravo" })
        {
            await CreateFolderAsync(client, root.Id, name);
        }

        Page first = (await client.GetFromJsonAsync<Page>($"/api/v1/items/{root.Id}/children?pageSize=2"))!;
        Page second = (await client.GetFromJsonAsync<Page>($"/api/v1/items/{root.Id}/children?pageSize=2&pageToken={first.NextPageToken}"))!;

        first.Items.Select(i => i.Name).ShouldBe(["alpha", "Bravo"]);
        first.NextPageToken.ShouldNotBeNull();
        second.Items.Select(i => i.Name).ShouldBe(["Charlie", "a-file.txt"]); // file luôn sau thư mục
        second.NextPageToken.ShouldBeNull();
    }

    [Fact]
    public async Task ListChildren_BySizeDescending_KeepsFoldersFirst()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        await UploadFileAsync(client, root.Id, "small.bin", 1);
        await UploadFileAsync(client, root.Id, "big.bin", 1000);
        await CreateFolderAsync(client, root.Id, "zzz");

        Page page = (await client.GetFromJsonAsync<Page>($"/api/v1/items/{root.Id}/children?orderBy=size&desc=true"))!;

        page.Items.Select(i => i.Name).ShouldBe(["zzz", "big.bin", "small.bin"]);
    }

    [Fact]
    public async Task Rename_WithStaleIfMatch_Returns412_AndWithCurrentRevision_Succeeds()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item folder = await CreateFolderAsync(client, root.Id, "cũ");

        var stale = await PatchAsync(client, folder.Id, new { name = "x" }, ifMatch: "\"7\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ErrorCode(stale)).ShouldBe("DRIVE.REVISION_MISMATCH");

        var ok = await PatchAsync(client, folder.Id, new { name = "mới" }, ifMatch: "\"1\"");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ok.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await Harness.Published.Any<ItemRenamed>(x => x.Context.Message.ItemId == folder.Id && x.Context.Message.NewName == "mới"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Rename_Root_Returns400()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);

        var response = await PatchAsync(client, root.Id, new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).ShouldBe("DRIVE.ROOT_IMMUTABLE");
    }

    [Fact]
    public async Task Move_Folder_UpdatesWholeSubtree()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item a = await CreateFolderAsync(client, root.Id, "A");
        Item b = await CreateFolderAsync(client, root.Id, "B");
        Item child = await CreateFolderAsync(client, a.Id, "child");
        Item grandChild = await CreateFolderAsync(client, child.Id, "grand");

        var move = await client.PostAsJsonAsync($"/api/v1/items/{child.Id}/move", new { newParentId = b.Id });

        move.StatusCode.ShouldBe(HttpStatusCode.OK);
        Item[] ancestors = (await client.GetFromJsonAsync<Item[]>($"/api/v1/items/{grandChild.Id}/ancestors"))!;
        ancestors.Select(x => x.Id).ShouldBe([root.Id, b.Id, child.Id]); // path của cháu đã đổi theo
        (await Harness.Published.Any<ItemMoved>(x =>
            x.Context.Message.ItemId == child.Id && x.Context.Message.OldParentId == a.Id && x.Context.Message.NewParentId == b.Id))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Move_IntoOwnDescendant_Returns409()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item parent = await CreateFolderAsync(client, root.Id, "parent");
        Item child = await CreateFolderAsync(client, parent.Id, "child");

        var response = await client.PostAsJsonAsync($"/api/v1/items/{parent.Id}/move", new { newParentId = child.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCode(response)).ShouldBe("DRIVE.INVALID_MOVE");
    }

    [Fact]
    public async Task Upload_Complete_CreatesFile_PublishesVersion_AndIsIdempotent()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);

        var start = await client.PostAsJsonAsync("/api/v1/uploads", new { parentId = root.Id, name = "báo cáo.pdf", sizeBytes = 1234, mimeType = "application/pdf" });
        start.StatusCode.ShouldBe(HttpStatusCode.Created);
        Upload upload = (await start.Content.ReadFromJsonAsync<Upload>())!;
        upload.Parts.ShouldHaveSingleItem().Url.ShouldStartWith("http://localhost/mycloud-blobs/"); // ký theo origin của request

        Item file = await CompleteAsync(client, upload.UploadId);
        Item again = await CompleteAsync(client, upload.UploadId);

        file.Kind.ShouldBe("file");
        file.Name.ShouldBe("báo cáo.pdf");
        file.SizeBytes.ShouldBe(1234);
        again.Id.ShouldBe(file.Id);
        (await Harness.Published.Any<FileVersionAdded>(x => x.Context.Message.ItemId == file.Id && x.Context.Message.SizeBytes == 1234))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Upload_SameName_IsRenamedAutomatically()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);

        Item first = await UploadFileAsync(client, root.Id, "report.pdf", 1);
        Item second = await UploadFileAsync(client, root.Id, "REPORT.pdf", 1);
        Item third = await UploadFileAsync(client, root.Id, "report.pdf", 1);

        first.Name.ShouldBe("report.pdf");
        second.Name.ShouldBe("REPORT (1).pdf");
        third.Name.ShouldBe("report (2).pdf");
    }

    [Fact]
    public async Task Upload_OverQuota_Returns413WithStorageCode()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        factory.Storage.QuotaBytes = 1000;
        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/uploads", new { parentId = root.Id, name = "big.iso", sizeBytes = 5000 });

            response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            (await ErrorCode(response)).ShouldBe("STORAGE.QUOTA_EXCEEDED");
        }
        finally
        {
            factory.Storage.QuotaBytes = long.MaxValue;
        }
    }

    [Fact]
    public async Task Upload_Abort_CallsStorage()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Upload upload = await StartUploadAsync(client, root.Id, "x.bin", 1);

        (await client.DeleteAsync($"/api/v1/uploads/{upload.UploadId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        factory.Storage.Aborted.ShouldContain(upload.UploadId);
    }

    [Fact]
    public async Task DownloadUrl_ForFile_ReturnsSignedLink_ForFolder_Returns400()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item file = await UploadFileAsync(client, root.Id, "ảnh.png", 5);

        var response = await client.PostAsJsonAsync($"/api/v1/items/{file.Id}/download-url", new { inline = true });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Link link = (await response.Content.ReadFromJsonAsync<Link>())!;
        link.Url.ShouldContain(Uri.EscapeDataString("ảnh.png"));
        link.Url.ShouldContain("inline=True");

        var folder = await client.PostAsJsonAsync($"/api/v1/items/{root.Id}/download-url", new { });
        folder.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OtherUser_CannotReadOrChange_AnyItem_Returns404()
    {
        // IDOR: B biết id thư mục / file / upload của A nhưng mọi thao tác đều nhận 404 như không tồn tại.
        using HttpClient a = NewUser();
        using HttpClient b = NewUser();
        Item rootA = await GetRootAsync(a);
        Item folderA = await CreateFolderAsync(a, rootA.Id, "của A");
        Item fileA = await UploadFileAsync(a, folderA.Id, "bí mật.txt", 10);
        Upload pendingA = await StartUploadAsync(a, rootA.Id, "đang tải.bin", 10);
        Item rootB = await GetRootAsync(b);

        HttpResponseMessage[] responses =
        [
            await b.GetAsync($"/api/v1/items/{folderA.Id}"),
            await b.GetAsync($"/api/v1/items/{folderA.Id}/children"),
            await b.GetAsync($"/api/v1/items/{fileA.Id}/ancestors"),
            await b.PostAsJsonAsync("/api/v1/folders", new { parentId = folderA.Id, name = "x" }),
            await PatchAsync(b, fileA.Id, new { name = "hacked.txt" }),
            await b.PostAsJsonAsync($"/api/v1/items/{fileA.Id}/move", new { newParentId = rootB.Id }),
            await b.PostAsJsonAsync($"/api/v1/items/{folderA.Id}/move", new { newParentId = rootB.Id }),
            await b.PostAsJsonAsync("/api/v1/uploads", new { parentId = folderA.Id, name = "x", sizeBytes = 1 }),
            await b.PostAsJsonAsync($"/api/v1/uploads/{pendingA.UploadId}/complete", new { parts = new[] { new { partNumber = 1, eTag = "\"x\"" } } }),
            await b.DeleteAsync($"/api/v1/uploads/{pendingA.UploadId}"),
            await b.PostAsJsonAsync($"/api/v1/items/{fileA.Id}/download-url", new { }),
        ];

        responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.NotFound);

        // A không chuyển được item của mình vào thư mục của B.
        (await a.PostAsJsonAsync($"/api/v1/items/{fileA.Id}/move", new { newParentId = rootB.Id })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetItemAsync(a, fileA.Id)).Name.ShouldBe("bí mật.txt");
    }

    [Fact]
    public async Task Request_WithoutToken_Returns401()
    {
        using HttpClient anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/items/root")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithTokenSignedByUnknownKey_Returns401()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateToken(Guid.NewGuid(), signWithUnknownKey: true));

        (await client.GetAsync("/api/v1/items/root")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WithExpiredToken_Returns401()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateToken(Guid.NewGuid(), expires: DateTimeOffset.UtcNow.AddMinutes(-5)));

        (await client.GetAsync("/api/v1/items/root")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_Ready_IsAnonymous_AndHealthy()
    {
        using HttpClient anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Item> GetRootAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Item>("/api/v1/items/root"))!;

    private static async Task<Item> GetItemAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Item>($"/api/v1/items/{id}"))!;

    private static async Task<Item> CreateFolderAsync(HttpClient client, Guid parentId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/folders", new { parentId, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Item>())!;
    }

    private static async Task<Upload> StartUploadAsync(HttpClient client, Guid parentId, string name, long size)
    {
        var response = await client.PostAsJsonAsync("/api/v1/uploads", new { parentId, name, sizeBytes = size, mimeType = "application/octet-stream" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Upload>())!;
    }

    private static async Task<Item> CompleteAsync(HttpClient client, Guid uploadId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/uploads/{uploadId}/complete", new { parts = new[] { new { partNumber = 1, eTag = "\"etag-1\"" } } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Item>())!;
    }

    private static async Task<Item> UploadFileAsync(HttpClient client, Guid parentId, string name, long size) =>
        await CompleteAsync(client, (await StartUploadAsync(client, parentId, name, size)).UploadId);

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/items/{id}") { Content = JsonContent.Create(body) };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}
