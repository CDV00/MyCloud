using System.Net;
using System.Net.Http.Json;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Media;
using MyCloud.Testing;

namespace Drive.IntegrationTests;

// Phase 5: thumbnail, tải dạng zip, tải cả thư mục lên (ADR-015).
public sealed partial class DriveApiTests
{
    private sealed record Thumbnail(Guid ItemId, string Url);

    private sealed record Thumbnails(Thumbnail[] Items, DateTimeOffset? ExpiresAt);

    private sealed record ArchiveRequested(Guid ArchiveId, string Name, int FileCount, long TotalBytes);

    private sealed record FolderTree(Dictionary<string, Guid> Folders, string[] Merged);

    private sealed record NameConflict(string Name, Guid ItemId, string Kind, string Role);

    private FakeStorageClient Storage => factory.Storage;

    [Fact]
    public async Task ThumbnailGenerated_MarksCurrentVersion_AndStaleEventsAreIgnored()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item photo = await UploadFileAsync(client, root.Id, "ảnh.png", 100);
        Guid v1 = await CurrentVersionIdAsync(client, photo.Id);
        Guid blob1 = (await BlobIdsAsync(photo.Id))[0];

        await PublishThumbnailAsync(photo, v1, blob1);

        (await WaitForThumbnailAsync(client, photo.Id)).ShouldBeTrue();
        Thumbnails thumbs = await ThumbnailsAsync(client, "/api/v1/thumbnails", [photo.Id, root.Id, Guid.NewGuid()]);
        thumbs.Items.ShouldHaveSingleItem().Url.ShouldContain($"/t/{blob1:N}/256.webp");

        // Nội dung mới ⇒ thumbnail cũ không còn đúng; thumbnail đến muộn của version cũ không bật lại cờ.
        await UploadNewVersionAsync(client, photo.Id, 200);
        (await GetItemAsync(client, photo.Id)).HasThumbnail.ShouldBeFalse();
        await PublishThumbnailAsync(photo, v1, blob1);
        (await GetItemAsync(client, photo.Id)).HasThumbnail.ShouldBeFalse();
        (await ThumbnailsAsync(client, "/api/v1/thumbnails", [photo.Id])).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Thumbnails_AreOnlyReturnedForItemsTheCallerCanView()
    {
        using HttpClient owner = NewUser();
        using HttpClient stranger = NewUser();
        Item root = await GetRootAsync(owner);
        Item album = await CreateFolderAsync(owner, root.Id, "Album");
        Item photo = await UploadFileAsync(owner, album.Id, "x.jpg", 10);
        await PublishThumbnailAsync(photo, await CurrentVersionIdAsync(owner, photo.Id), (await BlobIdsAsync(photo.Id))[0]);
        (await WaitForThumbnailAsync(owner, photo.Id)).ShouldBeTrue();

        (await ThumbnailsAsync(stranger, "/api/v1/thumbnails", [photo.Id])).Items.ShouldBeEmpty();

        // Khách mở link công khai của thư mục thấy thumbnail của file bên trong.
        HttpResponseMessage link = await owner.PostAsJsonAsync($"/api/v1/items/{album.Id}/links", new { role = "viewer" });
        link.EnsureSuccessStatusCode();
        string token = (await link.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["token"].ToString()!;
        using HttpClient anonymous = factory.CreateClient();
        (await ThumbnailsAsync(anonymous, $"/api/v1/public/links/{token}/thumbnails", [photo.Id])).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CreateArchive_WalksTheTree_ParentsFirst_SkippingTrash_AndRenamingDuplicateTopNames()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item project = await CreateFolderAsync(client, root.Id, "Dự án");
        Item sub = await CreateFolderAsync(client, project.Id, "Sub");
        await CreateFolderAsync(client, project.Id, "Trống");
        await UploadFileAsync(client, project.Id, "a.txt", 10);
        await UploadFileAsync(client, sub.Id, "b.txt", 20);
        Item trashed = await UploadFileAsync(client, sub.Id, "deleted.txt", 30);
        (await client.PostAsync($"/api/v1/items/{trashed.Id}/trash", null)).EnsureSuccessStatusCode();
        Item other = await CreateFolderAsync(client, sub.Id, "dự án"); // trùng tên (không phân biệt hoa thường) ở cấp cao nhất

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/archives", new { itemIds = new[] { project.Id, other.Id } });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ArchiveRequested archive = (await response.Content.ReadFromJsonAsync<ArchiveRequested>())!;
        archive.FileCount.ShouldBe(2);
        archive.TotalBytes.ShouldBe(30);
        archive.Name.ShouldStartWith("MyCloud-");
        var (userId, _, entries) = Storage.Archives[archive.ArchiveId];
        userId.ShouldBe(root.OwnerId);
        string[] paths = entries.Select(e => e.Path).ToArray();
        paths.ShouldBe(["Dự án/", "Dự án/Sub/", "Dự án/Trống/", "Dự án/a.txt", "Dự án/Sub/b.txt", "Dự án/Sub/dự án/", "dự án (1)/"], ignoreOrder: true);
        Array.IndexOf(paths, "Dự án/Sub/").ShouldBeLessThan(Array.IndexOf(paths, "Dự án/Sub/b.txt"));
        entries.Single(e => e.Path == "Dự án/Sub/b.txt").BlobId.ShouldNotBeNull();
        entries.Single(e => e.Path == "Dự án/Sub/").BlobId.ShouldBeNull();
    }

    [Fact]
    public async Task CreateArchive_OfOneFolder_IsNamedAfterIt_AndStrangersGet404()
    {
        using HttpClient client = NewUser();
        using HttpClient stranger = NewUser();
        Item root = await GetRootAsync(client);
        Item folder = await CreateFolderAsync(client, root.Id, "Ảnh");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/archives", new { itemIds = new[] { folder.Id } });
        (await response.Content.ReadFromJsonAsync<ArchiveRequested>())!.Name.ShouldBe("Ảnh.zip");

        (await stranger.PostAsJsonAsync("/api/v1/archives", new { itemIds = new[] { folder.Id } })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateFolderTree_CreatesMissingParents_AndRenamesOrMergesOnConflict()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item existing = await CreateFolderAsync(client, root.Id, "Photos");
        Item existingChild = await CreateFolderAsync(client, existing.Id, "2024");

        FolderTree renamed = await FolderTreeAsync(client, root.Id, ["Photos/2024/Trip", "Docs"], "rename");
        renamed.Merged.ShouldBeEmpty();
        Item copy = await GetItemAsync(client, (await GetAncestorIdsAsync(client, renamed.Folders["Photos/2024/Trip"]))[1]);
        copy.Name.ShouldBe("Photos (1)");
        (await GetItemAsync(client, renamed.Folders["Docs"])).ParentId.ShouldBe(root.Id);

        FolderTree merged = await FolderTreeAsync(client, root.Id, ["photos", "Photos/2024", "Photos/2024/Trip"], "merge");
        merged.Folders["photos"].ShouldBe(existing.Id);
        merged.Folders["Photos/2024"].ShouldBe(existingChild.Id);
        merged.Merged.ShouldBe(["photos", "Photos/2024"], ignoreOrder: true);
        Item trip = await GetItemAsync(client, merged.Folders["Photos/2024/Trip"]);
        trip.ParentId.ShouldBe(existingChild.Id);
        trip.Name.ShouldBe("Trip");
    }

    [Fact]
    public async Task CreateFolderTree_InvalidNames_AreRejected_WithoutCreatingAnything()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/folders/tree", new { parentId = root.Id, paths = new[] { "Ok", "Ok/.." } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).ShouldBe("DRIVE.INVALID_NAME");
        (await ChildNamesAsync(client, root.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task NameConflicts_ReturnsExistingItems_IgnoringCaseAndTrash()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item report = await UploadFileAsync(client, root.Id, "Báo cáo.pdf", 1);
        Item photos = await CreateFolderAsync(client, root.Id, "Photos");
        Item gone = await UploadFileAsync(client, root.Id, "gone.txt", 1);
        (await client.PostAsync($"/api/v1/items/{gone.Id}/trash", null)).EnsureSuccessStatusCode();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/items/{root.Id}/name-conflicts", new { names = new[] { "BÁO CÁO.pdf", "photos", "gone.txt", "new.txt", "bad/name" } });

        NameConflict[] conflicts = (await response.Content.ReadFromJsonAsync<NameConflict[]>())!;
        conflicts.Select(c => (c.Name, c.ItemId, c.Kind)).ShouldBe(
            [("BÁO CÁO.pdf", report.Id, "file"), ("photos", photos.Id, "folder")], ignoreOrder: true);
        conflicts.ShouldAllBe(c => c.Role == "owner");
    }

    private async Task PublishThumbnailAsync(Item item, Guid versionId, Guid blobId)
    {
        var message = new ThumbnailGenerated(item.Id, item.OwnerId, versionId, blobId, [256, 1024]);
        await Harness.Bus.Publish(message);

        // Consumed.Any có thể trả về trước khi bản tin tới consumer (harness chưa thấy hoạt động) ⇒ tự đợi.
        for (int i = 0; i < 100 && !Harness.Consumed.Select<ThumbnailGenerated>(x => x.Context.Message.EventId == message.EventId).Any(); i++)
        {
            await Task.Delay(50);
        }

        Harness.Consumed.Select<ThumbnailGenerated>(x => x.Context.Message.EventId == message.EventId).Any().ShouldBeTrue();
    }

    /// <summary>Consumer chạy trong transaction của inbox: đợi tới khi thay đổi đã commit.</summary>
    private static async Task<bool> WaitForThumbnailAsync(HttpClient client, Guid itemId)
    {
        for (int i = 0; i < 50; i++)
        {
            if ((await GetItemAsync(client, itemId)).HasThumbnail)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<Thumbnails> ThumbnailsAsync(HttpClient client, string url, Guid[] itemIds)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(url, new { itemIds, size = 256 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Thumbnails>())!;
    }

    private static async Task<FolderTree> FolderTreeAsync(HttpClient client, Guid parentId, string[] paths, string conflict)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/folders/tree", new { parentId, paths, conflict });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderTree>())!;
    }

    private static async Task<Guid[]> GetAncestorIdsAsync(HttpClient client, Guid itemId) =>
        (await client.GetFromJsonAsync<Item[]>($"/api/v1/items/{itemId}/ancestors"))!.Select(i => i.Id).ToArray();
}

// Trình sửa file: lưu = phiên bản mới, kèm revision lúc mở để không ghi đè âm thầm thay đổi của người khác.
public sealed partial class DriveApiTests
{
    [Fact]
    public async Task SavingAnEdit_WithAStaleRevision_Returns412_AndTheCurrentRevisionSucceeds()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item file = await UploadFileAsync(client, root.Id, "ghi chú.txt", 10);
        long opened = (await GetItemAsync(client, file.Id)).Revision;

        // Người khác lưu trước.
        await UploadNewVersionAsync(client, file.Id, 20);

        HttpResponseMessage stale = await client.PostAsJsonAsync(
            "/api/v1/uploads", new { targetItemId = file.Id, sizeBytes = 30, mimeType = "text/plain", expectedRevision = opened });
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ErrorCode(stale)).ShouldBe("DRIVE.REVISION_MISMATCH");

        long current = (await GetItemAsync(client, file.Id)).Revision;
        HttpResponseMessage fresh = await client.PostAsJsonAsync(
            "/api/v1/uploads", new { targetItemId = file.Id, sizeBytes = 30, mimeType = "text/plain", expectedRevision = current });
        fresh.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}

// Lịch sử chỉnh sửa: mỗi phiên bản ghi cách tạo (tải lên / sửa trong trình duyệt / khôi phục / sao chép).
public sealed partial class DriveApiTests
{
    private sealed record HistoryEntry(Guid Id, int VersionNo, Guid CreatedBy, bool IsCurrent, string Source, int? RestoredFromVersionNo);

    [Fact]
    public async Task EditHistory_RecordsHowEachVersionWasMade_AndPublishesTheSource()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item file = await UploadFileAsync(client, root.Id, "ghi chú.txt", 10);

        HttpResponseMessage started = await client.PostAsJsonAsync(
            "/api/v1/uploads", new { targetItemId = file.Id, sizeBytes = 20, mimeType = "text/plain", source = "edit" });
        started.EnsureSuccessStatusCode();
        await CompleteAsync(client, (await started.Content.ReadFromJsonAsync<Upload>())!.UploadId);

        HistoryEntry[] history = (await client.GetFromJsonAsync<HistoryEntry[]>($"/api/v1/items/{file.Id}/versions"))!;
        (await client.PostAsync($"/api/v1/items/{file.Id}/versions/{history.Single(v => v.VersionNo == 1).Id}/restore", null))
            .EnsureSuccessStatusCode();
        Item copy = await CopyAsync(client, file.Id, new { name = "bản sao.txt" });

        history = (await client.GetFromJsonAsync<HistoryEntry[]>($"/api/v1/items/{file.Id}/versions"))!;
        history.Select(v => (v.VersionNo, v.Source, v.RestoredFromVersionNo)).ShouldBe([(3, "restore", (int?)1), (2, "edit", null), (1, "upload", null)]);
        history.ShouldAllBe(v => v.CreatedBy == file.OwnerId);
        (await client.GetFromJsonAsync<HistoryEntry[]>($"/api/v1/items/{copy.Id}/versions"))!.ShouldHaveSingleItem().Source.ShouldBe("copy");

        (await Harness.Published.Any<MyCloud.Contracts.Events.Drive.FileVersionAdded>(x =>
            x.Context.Message.ItemId == file.Id && x.Context.Message.VersionNo == 2 && x.Context.Message.Source == "edit")).ShouldBeTrue();
        (await Harness.Published.Any<MyCloud.Contracts.Events.Drive.FileVersionAdded>(x =>
            x.Context.Message.ItemId == file.Id && x.Context.Message.Source == "restore" && x.Context.Message.RestoredFromVersionNo == 1)).ShouldBeTrue();
    }
}
