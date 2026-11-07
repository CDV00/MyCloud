using System.Net;
using System.Net.Http.Json;
using Drive.Application.Abstractions;
using Drive.Infrastructure.Maintenance;
using Drive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Testing;

namespace Drive.IntegrationTests;

// Phase 3: thùng rác, version, gắn sao, sao chép, "Gần đây".
public sealed partial class DriveApiTests
{
    private sealed record Version(Guid Id, int VersionNo, long SizeBytes, string MimeType, bool IsCurrent);

    [Fact]
    public async Task Trash_Folder_HidesWholeGroup_AndRestoreBringsBackOnlyThatGroup()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item a = await CreateFolderAsync(client, root.Id, "A");
        Item b = await CreateFolderAsync(client, a.Id, "B");
        Item inA = await UploadFileAsync(client, a.Id, "in-a.txt", 1);
        Item inB = await UploadFileAsync(client, b.Id, "in-b.txt", 1);
        Item deletedEarlier = await UploadFileAsync(client, a.Id, "old.txt", 1);
        (await PostAsync(client, $"/api/v1/items/{deletedEarlier.Id}/trash")).EnsureSuccessStatusCode();

        HttpResponseMessage trashed = await PostAsync(client, $"/api/v1/items/{a.Id}/trash");

        trashed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await trashed.Content.ReadFromJsonAsync<Item>())!.TrashedAt.ShouldNotBeNull();
        (await ChildNamesAsync(client, root.Id)).ShouldBeEmpty();
        (await ListAsync(client, "/api/v1/trash")).Select(i => i.Name).ShouldBe(["A", "old.txt"]); // mới xoá trước
        // Mở thư mục trong thùng rác: thấy các item bị xoá cùng nó, không thấy item đã xoá riêng trước đó.
        (await ChildNamesAsync(client, a.Id)).ShouldBe(["B", "in-a.txt"]);
        (await GetItemAsync(client, inB.Id)).TrashedAt.ShouldNotBeNull();
        (await Harness.Published.Any<ItemTrashed>(x => x.Context.Message.ItemId == a.Id)).ShouldBeTrue();

        (await PostAsync(client, $"/api/v1/items/{a.Id}/restore")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ChildNamesAsync(client, root.Id)).ShouldBe(["A"]);
        (await ChildNamesAsync(client, a.Id)).ShouldBe(["B", "in-a.txt"]);
        (await GetItemAsync(client, inA.Id)).TrashedAt.ShouldBeNull();
        (await GetItemAsync(client, inB.Id)).TrashedAt.ShouldBeNull();
        (await ListAsync(client, "/api/v1/trash")).Select(i => i.Name).ShouldBe(["old.txt"]);
        (await Harness.Published.Any<ItemRestored>(x => x.Context.Message.ItemId == a.Id && x.Context.Message.ParentId == root.Id))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task TrashedItems_CannotBeChanged_Returns409()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item folder = await CreateFolderAsync(client, root.Id, "F");
        Item file = await UploadFileAsync(client, root.Id, "f.txt", 1);
        await PostAsync(client, $"/api/v1/items/{folder.Id}/trash");
        await PostAsync(client, $"/api/v1/items/{file.Id}/trash");

        HttpResponseMessage[] responses =
        [
            await PatchAsync(client, file.Id, new { name = "x.txt" }),
            await client.PostAsJsonAsync("/api/v1/folders", new { parentId = folder.Id, name = "inside" }),
            await client.PostAsJsonAsync("/api/v1/uploads", new { parentId = folder.Id, name = "x.bin", sizeBytes = 1 }),
            await client.PostAsJsonAsync($"/api/v1/items/{file.Id}/move", new { newParentId = root.Id }),
            await client.PutAsync($"/api/v1/items/{file.Id}/star", null),
        ];

        foreach (HttpResponseMessage response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ErrorCode(response)).ShouldBe("DRIVE.ITEM_TRASHED");
        }

        // Vẫn xem / tải được để quyết định có khôi phục không.
        (await PostAsync(client, $"/api/v1/items/{file.Id}/download-url")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Restore_WhenParentIsInTrash_GoesToMyDrive_AndAvoidsNameConflict()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item parent = await CreateFolderAsync(client, root.Id, "P");
        Item file = await UploadFileAsync(client, parent.Id, "x.txt", 1);
        await PostAsync(client, $"/api/v1/items/{file.Id}/trash");
        await PostAsync(client, $"/api/v1/items/{parent.Id}/trash");
        await UploadFileAsync(client, root.Id, "x.txt", 1); // tên cũ đã có người dùng ở "Drive của tôi"

        Item restored = (await (await PostAsync(client, $"/api/v1/items/{file.Id}/restore")).Content.ReadFromJsonAsync<Item>())!;

        restored.ParentId.ShouldBe(root.Id);
        restored.Name.ShouldBe("x (1).txt");
        restored.TrashedAt.ShouldBeNull();
    }

    [Fact]
    public async Task TrashedItem_FreesItsName()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item docs = await CreateFolderAsync(client, root.Id, "Docs");
        await PostAsync(client, $"/api/v1/items/{docs.Id}/trash");

        (await client.PostAsJsonAsync("/api/v1/folders", new { parentId = root.Id, name = "Docs" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        Item restored = (await (await PostAsync(client, $"/api/v1/items/{docs.Id}/restore")).Content.ReadFromJsonAsync<Item>())!;

        restored.Name.ShouldBe("Docs (1)");
    }

    [Fact]
    public async Task Purge_RequiresTrash_ThenDeletesSubtree_AndPublishesEveryBlob()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item folder = await CreateFolderAsync(client, root.Id, "F");
        Item file = await UploadFileAsync(client, folder.Id, "a.txt", 10);
        await UploadNewVersionAsync(client, file.Id, 20);
        Guid[] blobs = await BlobIdsAsync(file.Id);
        blobs.Length.ShouldBe(2);

        HttpResponseMessage notTrashed = await client.DeleteAsync($"/api/v1/items/{folder.Id}");
        notTrashed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCode(notTrashed)).ShouldBe("DRIVE.NOT_IN_TRASH");

        await PostAsync(client, $"/api/v1/items/{folder.Id}/trash");
        (await client.DeleteAsync($"/api/v1/items/{folder.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync($"/api/v1/items/{folder.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/items/{file.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Harness.Published.Any<ItemsPurged>(x =>
            x.Context.Message.ItemIds.Order().SequenceEqual(new[] { folder.Id, file.Id }.Order())
            && x.Context.Message.BlobIds.Order().SequenceEqual(blobs.Order()))).ShouldBeTrue();
    }

    [Fact]
    public async Task EmptyTrash_PurgesEveryGroup()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item folder = await CreateFolderAsync(client, root.Id, "F");
        Item nested = await UploadFileAsync(client, folder.Id, "nested.txt", 1);
        Item loose = await UploadFileAsync(client, root.Id, "loose.txt", 1);
        await PostAsync(client, $"/api/v1/items/{nested.Id}/trash"); // nhóm riêng nằm trong thư mục sẽ bị xoá
        await PostAsync(client, $"/api/v1/items/{folder.Id}/trash");
        await PostAsync(client, $"/api/v1/items/{loose.Id}/trash");

        HttpResponseMessage response = await client.DeleteAsync("/api/v1/trash");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PurgeResult>())!.Purged.ShouldBe(3);
        (await ListAsync(client, "/api/v1/trash")).ShouldBeEmpty();
    }

    [Fact]
    public async Task TrashRetentionJob_PurgesOnlyExpiredGroups()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item expired = await UploadFileAsync(client, root.Id, "expired.txt", 1);
        Item recent = await UploadFileAsync(client, root.Id, "recent.txt", 1);
        await PostAsync(client, $"/api/v1/items/{expired.Id}/trash");
        await PostAsync(client, $"/api/v1/items/{recent.Id}/trash");
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DriveDbContext>();
            await db.Items.Where(i => i.Id == expired.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.TrashedAt, DateTimeOffset.UtcNow.AddDays(-31)));
        }

        await factory.Services.GetRequiredService<TrashRetentionJob>().RunOnceAsync(CancellationToken.None);

        (await client.GetAsync($"/api/v1/items/{expired.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(client, "/api/v1/trash")).Select(i => i.Id).ShouldBe([recent.Id]);
    }

    [Fact]
    public async Task Versions_UploadNew_List_RestoreOld_DeleteOld()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item file = await UploadFileAsync(client, root.Id, "doc.txt", 10);

        Item v2 = await UploadNewVersionAsync(client, file.Id, 20);
        v2.Id.ShouldBe(file.Id);
        v2.Name.ShouldBe("doc.txt");
        v2.VersionNo.ShouldBe(2);
        v2.SizeBytes.ShouldBe(20);
        (await ChildNamesAsync(client, root.Id)).ShouldBe(["doc.txt"]); // không tạo file mới

        Version[] versions = (await client.GetFromJsonAsync<Version[]>($"/api/v1/items/{file.Id}/versions"))!;
        versions.Select(v => (v.VersionNo, v.SizeBytes, v.IsCurrent)).ShouldBe([(2, 20L, true), (1, 10L, false)]);

        // Khôi phục v1 = version mới (v3) có nội dung của v1, Storage sao chép blob.
        Guid v1Blob = (await BlobIdsAsync(file.Id))[0];
        Item v3 = (await (await PostAsync(client, $"/api/v1/items/{file.Id}/versions/{versions[1].Id}/restore")).Content.ReadFromJsonAsync<Item>())!;
        v3.VersionNo.ShouldBe(3);
        v3.SizeBytes.ShouldBe(10);
        factory.Storage.Copies.ShouldContain(c => c.SourceBlobId == v1Blob);

        HttpResponseMessage deleteCurrent = await client.DeleteAsync($"/api/v1/items/{file.Id}/versions/{(await CurrentVersionIdAsync(client, file.Id))}");
        deleteCurrent.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCode(deleteCurrent)).ShouldBe("DRIVE.CANNOT_DELETE_CURRENT_VERSION");

        (await client.DeleteAsync($"/api/v1/items/{file.Id}/versions/{versions[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<Version[]>($"/api/v1/items/{file.Id}/versions"))!.Select(v => v.VersionNo).ShouldBe([3, 1]);
        (await Harness.Published.Any<FileVersionsDeleted>(x => x.Context.Message.VersionIds.SequenceEqual(new[] { versions[0].Id }))).ShouldBeTrue();

        // Tải một version cũ cụ thể; version của file khác ⇒ 404.
        (await PostAsync(client, $"/api/v1/items/{file.Id}/download-url", new { versionId = versions[1].Id })).StatusCode.ShouldBe(HttpStatusCode.OK);
        HttpResponseMessage unknown = await PostAsync(client, $"/api/v1/items/{file.Id}/download-url", new { versionId = Guid.NewGuid() });
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCode(unknown)).ShouldBe("DRIVE.VERSION_NOT_FOUND");
    }

    [Fact]
    public async Task Versions_BeyondConfiguredLimit_OldestAreDeleted()
    {
        DriveOptions options = factory.Services.GetRequiredService<DriveOptions>();
        int original = options.MaxVersionsPerFile;
        options.MaxVersionsPerFile = 2;
        try
        {
            using HttpClient client = NewUser();
            Item root = await GetRootAsync(client);
            Item file = await UploadFileAsync(client, root.Id, "doc.txt", 1);
            Guid firstBlob = (await BlobIdsAsync(file.Id)).Single();
            await UploadNewVersionAsync(client, file.Id, 2);
            await UploadNewVersionAsync(client, file.Id, 3);

            (await client.GetFromJsonAsync<Version[]>($"/api/v1/items/{file.Id}/versions"))!.Select(v => v.VersionNo).ShouldBe([3, 2]);
            (await Harness.Published.Any<FileVersionsDeleted>(x => x.Context.Message.ItemId == file.Id
                && x.Context.Message.BlobIds.SequenceEqual(new[] { firstBlob }))).ShouldBeTrue();
        }
        finally
        {
            options.MaxVersionsPerFile = original;
        }
    }

    [Fact]
    public async Task Stars_AreIdempotent_ListedNewestFirst_AndHiddenWhileTrashed()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item folder = await CreateFolderAsync(client, root.Id, "F");
        Item file = await UploadFileAsync(client, root.Id, "a.txt", 1);

        (await client.PutAsync($"/api/v1/items/{file.Id}/star", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsync($"/api/v1/items/{folder.Id}/star", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsync($"/api/v1/items/{folder.Id}/star", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(client, "/api/v1/starred")).Select(i => i.Name).ShouldBe(["F", "a.txt"]);
        (await GetItemAsync(client, file.Id)).Starred.ShouldBeTrue();
        (await client.GetFromJsonAsync<Page>($"/api/v1/items/{root.Id}/children"))!.Items.ShouldAllBe(i => i.Starred);

        await PostAsync(client, $"/api/v1/items/{folder.Id}/trash");
        (await ListAsync(client, "/api/v1/starred")).Select(i => i.Name).ShouldBe(["a.txt"]);
        await PostAsync(client, $"/api/v1/items/{folder.Id}/restore");
        (await ListAsync(client, "/api/v1/starred")).Select(i => i.Name).ShouldBe(["F", "a.txt"]);

        (await client.DeleteAsync($"/api/v1/items/{file.Id}/star")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(client, "/api/v1/starred")).Select(i => i.Name).ShouldBe(["F"]);
        (await client.PutAsync($"/api/v1/items/{root.Id}/star", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Copy_File_AndFolderTree_WithUniqueNames()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item a = await CreateFolderAsync(client, root.Id, "A");
        Item b = await CreateFolderAsync(client, a.Id, "B");
        Item f1 = await UploadFileAsync(client, a.Id, "one.txt", 5);
        await UploadFileAsync(client, b.Id, "two.txt", 7);
        Item gone = await UploadFileAsync(client, a.Id, "gone.txt", 1);
        await PostAsync(client, $"/api/v1/items/{gone.Id}/trash"); // item trong thùng rác không được sao chép

        Item fileCopy = await CopyAsync(client, f1.Id, new { });
        Item named = await CopyAsync(client, f1.Id, new { targetParentId = root.Id, name = "Bản sao của one.txt" });
        Item folderCopy = await CopyAsync(client, a.Id, new { targetParentId = root.Id });

        fileCopy.Name.ShouldBe("one (1).txt");
        fileCopy.ParentId.ShouldBe(a.Id);
        fileCopy.SizeBytes.ShouldBe(5);
        named.Name.ShouldBe("Bản sao của one.txt");
        folderCopy.Name.ShouldBe("A (1)");
        (await ChildNamesAsync(client, folderCopy.Id)).ShouldBe(["B", "one (1).txt", "one.txt"]);
        Item copiedB = (await client.GetFromJsonAsync<Page>($"/api/v1/items/{folderCopy.Id}/children"))!.Items.Single(i => i.Name == "B");
        Item copiedTwo = (await client.GetFromJsonAsync<Page>($"/api/v1/items/{copiedB.Id}/children"))!.Items.ShouldHaveSingleItem();
        copiedTwo.Name.ShouldBe("two.txt");
        copiedTwo.SizeBytes.ShouldBe(7);
        (await Harness.Published.Any<FileVersionAdded>(x => x.Context.Message.ItemId == copiedTwo.Id)).ShouldBeTrue(); // để Storage claim
    }

    [Fact]
    public async Task Copy_TooManyItems_OrOverQuota_Returns413()
    {
        using HttpClient client = NewUser();
        Item root = await GetRootAsync(client);
        Item a = await CreateFolderAsync(client, root.Id, "A");
        await UploadFileAsync(client, a.Id, "1.txt", 600);
        await UploadFileAsync(client, a.Id, "2.txt", 600);

        DriveOptions options = factory.Services.GetRequiredService<DriveOptions>();
        int original = options.MaxCopyItems;
        options.MaxCopyItems = 2;
        try
        {
            HttpResponseMessage tooLarge = await PostAsync(client, $"/api/v1/items/{a.Id}/copy", new { });
            tooLarge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            (await ErrorCode(tooLarge)).ShouldBe("DRIVE.COPY_TOO_LARGE");
        }
        finally
        {
            options.MaxCopyItems = original;
        }

        factory.Storage.QuotaBytes = 1000;
        try
        {
            HttpResponseMessage overQuota = await PostAsync(client, $"/api/v1/items/{a.Id}/copy", new { });
            overQuota.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            (await ErrorCode(overQuota)).ShouldBe("STORAGE.QUOTA_EXCEEDED");
            (await ChildNamesAsync(client, root.Id)).ShouldBe(["A"]); // không để lại bản sao dở
        }
        finally
        {
            factory.Storage.QuotaBytes = long.MaxValue;
        }
    }

    [Fact]
    public async Task DownloadUrl_PublishesItemAccessed_AtMostOncePerFiveMinutes()
    {
        Guid userId = Guid.NewGuid();
        using HttpClient client = factory.CreateClient().AsUser(userId);
        Item root = await GetRootAsync(client);
        Item file = await UploadFileAsync(client, root.Id, "a.txt", 1);

        await PostAsync(client, $"/api/v1/items/{file.Id}/download-url", new { inline = true });
        await PostAsync(client, $"/api/v1/items/{file.Id}/download-url");

        (await Harness.Published.Any<ItemAccessed>(x => x.Context.Message.ItemId == file.Id
            && x.Context.Message.UserId == userId && x.Context.Message.Action == "open")).ShouldBeTrue();
        await Task.Delay(TimeSpan.FromSeconds(2)); // cho outbox giao nốt nếu (sai) có bản tin thứ hai
        Harness.Published.Select<ItemAccessed>(x => x.Context.Message.ItemId == file.Id).Count().ShouldBe(1);
    }

    [Fact]
    public async Task OtherUser_CannotUseTrashVersionStarOrCopy_OnMyItems_Returns404()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item file = await UploadFileAsync(owner, root.Id, "secret.txt", 1);
        Item trashed = await UploadFileAsync(owner, root.Id, "trashed.txt", 1);
        await PostAsync(owner, $"/api/v1/items/{trashed.Id}/trash");
        await owner.PutAsync($"/api/v1/items/{file.Id}/star", null);
        Guid versionId = await CurrentVersionIdAsync(owner, file.Id);

        using HttpClient intruder = NewUser();
        Item intruderRoot = await GetRootAsync(intruder);
        HttpResponseMessage[] responses =
        [
            await PostAsync(intruder, $"/api/v1/items/{file.Id}/trash"),
            await PostAsync(intruder, $"/api/v1/items/{trashed.Id}/restore"),
            await intruder.DeleteAsync($"/api/v1/items/{trashed.Id}"),
            await intruder.PutAsync($"/api/v1/items/{file.Id}/star", null),
            await intruder.DeleteAsync($"/api/v1/items/{file.Id}/star"),
            await PostAsync(intruder, $"/api/v1/items/{file.Id}/copy", new { targetParentId = intruderRoot.Id }),
            await intruder.GetAsync($"/api/v1/items/{file.Id}/versions"),
            await PostAsync(intruder, $"/api/v1/items/{file.Id}/versions/{versionId}/restore"),
            await intruder.DeleteAsync($"/api/v1/items/{file.Id}/versions/{versionId}"),
            await PostAsync(intruder, $"/api/v1/items/{file.Id}/download-url", new { versionId }),
            await intruder.PostAsJsonAsync("/api/v1/uploads", new { targetItemId = file.Id, sizeBytes = 1 }),
        ];

        responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.NotFound);
        (await ListAsync(intruder, "/api/v1/trash")).ShouldBeEmpty();
        (await ListAsync(intruder, "/api/v1/starred")).ShouldBeEmpty();
        (await GetItemAsync(owner, trashed.Id)).TrashedAt.ShouldNotBeNull(); // không bị khôi phục / xoá
    }

    private sealed record PurgeResult(int Purged);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { });

    private static async Task<Item[]> ListAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<Page>(url))!.Items;

    private static async Task<string[]> ChildNamesAsync(HttpClient client, Guid folderId) =>
        (await ListAsync(client, $"/api/v1/items/{folderId}/children")).Select(i => i.Name).ToArray();

    private static async Task<Item> CopyAsync(HttpClient client, Guid itemId, object body)
    {
        HttpResponseMessage response = await PostAsync(client, $"/api/v1/items/{itemId}/copy", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Item>())!;
    }

    private static async Task<Item> UploadNewVersionAsync(HttpClient client, Guid fileId, long size)
    {
        HttpResponseMessage started = await client.PostAsJsonAsync("/api/v1/uploads", new { targetItemId = fileId, sizeBytes = size, mimeType = "text/plain" });
        started.EnsureSuccessStatusCode();
        return await CompleteAsync(client, (await started.Content.ReadFromJsonAsync<Upload>())!.UploadId);
    }

    private static async Task<Guid> CurrentVersionIdAsync(HttpClient client, Guid fileId) =>
        (await client.GetFromJsonAsync<Version[]>($"/api/v1/items/{fileId}/versions"))!.Single(v => v.IsCurrent).Id;

    private async Task<Guid[]> BlobIdsAsync(Guid itemId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DriveDbContext>();
        return await db.FileVersions.Where(v => v.ItemId == itemId).OrderBy(v => v.VersionNo).Select(v => v.BlobId).ToArrayAsync();
    }
}
