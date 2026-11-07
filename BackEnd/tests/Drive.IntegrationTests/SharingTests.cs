using System.Net;
using System.Net.Http.Json;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Contracts.Events.Sharing;
using MyCloud.Testing;
using Sharing.Api.Data;

namespace Drive.IntegrationTests;

// Phase 4: chia sẻ qua Drive (mặt tiền REST) + Sharing thật (gRPC) — docs/03 §5, ADR-014.
public sealed partial class DriveApiTests
{
    private sealed record Person(Guid Id, string DisplayName, string Email);

    private sealed record PermissionView(Guid Id, Person User, string Role, InheritedFrom? InheritedFrom);

    private sealed record InheritedFrom(Guid Id, string Name);

    private sealed record LinkView(Guid Id, string Role, bool HasPassword);

    private sealed record SharingView(Person? Owner, string MyRole, bool CanShare, PermissionView[] Permissions, LinkView[] Links);

    private sealed record CreatedLink(LinkView Link, string Token);

    /// <summary>
    /// Ma trận quyền (docs/07 Phase 4): vai trò × (trực tiếp / kế thừa) × (xem / sửa / chia sẻ / xoá). Không có quyền
    /// ⇒ 404 như item không tồn tại; thấy được nhưng thiếu quyền ⇒ 403; chỉ chủ sở hữu được xoá.
    /// </summary>
    [Theory]
    [InlineData("owner", "direct")]
    [InlineData("editor", "direct")]
    [InlineData("editor", "inherited")]
    [InlineData("commenter", "direct")]
    [InlineData("commenter", "inherited")]
    [InlineData("viewer", "direct")]
    [InlineData("viewer", "inherited")]
    [InlineData("none", "direct")]
    public async Task PermissionMatrix(string role, string grant)
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "Dự án");
        Item file = await UploadFileAsync(owner, folder.Id, "kế hoạch.txt", 10);

        Guid actorId = Guid.NewGuid();
        using HttpClient actor = role == "owner" ? owner : factory.CreateClient().AsUser(actorId);
        if (role is not ("owner" or "none"))
        {
            await ShareAsync(owner, grant == "direct" ? file.Id : folder.Id, actorId, role);
        }

        (HttpStatusCode View, HttpStatusCode Edit, HttpStatusCode Share, HttpStatusCode Delete) expected = role switch
        {
            "owner" => (HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK),
            "editor" => (HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            "commenter" or "viewer" => (HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            _ => (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound),
        };

        (await actor.GetAsync($"/api/v1/items/{file.Id}")).StatusCode.ShouldBe(expected.View);
        (await PatchAsync(actor, file.Id, new { name = "đổi tên.txt" })).StatusCode.ShouldBe(expected.Edit);
        (await actor.PostAsJsonAsync($"/api/v1/items/{file.Id}/permissions", new { email = FakeUserDirectory.EmailOf(Guid.NewGuid()), role = "viewer" }))
            .StatusCode.ShouldBe(expected.Share);
        (await PostAsync(actor, $"/api/v1/items/{file.Id}/trash")).StatusCode.ShouldBe(expected.Delete);

        if (expected.Edit == HttpStatusCode.Forbidden)
        {
            (await ErrorCode(await PatchAsync(actor, file.Id, new { name = "x.txt" }))).ShouldBe("DRIVE.ACCESS_DENIED");
        }
    }

    [Fact]
    public async Task Share_ByEmail_ListsDirectAndInherited_PublishesItemShared_AndRoleIsReported()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "Chung");
        Item file = await UploadFileAsync(owner, folder.Id, "a.txt", 1);
        Guid bob = Guid.NewGuid(), carol = Guid.NewGuid();

        HttpResponseMessage shared = await owner.PostAsJsonAsync(
            $"/api/v1/items/{folder.Id}/permissions", new { email = FakeUserDirectory.EmailOf(bob), role = "editor", message = "Xem giúp nhé" });
        shared.StatusCode.ShouldBe(HttpStatusCode.OK);
        await ShareAsync(owner, file.Id, carol, "viewer");

        SharingView view = (await owner.GetFromJsonAsync<SharingView>($"/api/v1/items/{file.Id}/sharing"))!;
        view.MyRole.ShouldBe("owner");
        view.CanShare.ShouldBeTrue();
        view.Permissions.Select(p => (p.User.Id, p.Role, p.InheritedFrom?.Name)).ShouldBe([(carol, "viewer", null), (bob, "editor", "Chung")]);

        ITestHarness sharing = factory.Sharing.Services.GetRequiredService<ITestHarness>();
        (await sharing.Published.Any<ItemShared>(x => x.Context.Message.ItemId == folder.Id && x.Context.Message.GranteeId == bob
            && x.Context.Message.ItemName == "Chung" && x.Context.Message.Role == "editor" && x.Context.Message.Message == "Xem giúp nhé"
            && x.Context.Message.GranteeEmail == FakeUserDirectory.EmailOf(bob) && x.Context.Message.SharedByName!.StartsWith("User ", StringComparison.Ordinal)))
            .ShouldBeTrue();

        using HttpClient bobClient = factory.CreateClient().AsUser(bob);
        (await GetItemAsync(bobClient, file.Id)).Role.ShouldBe("editor"); // kế thừa từ thư mục
        using HttpClient carolClient = factory.CreateClient().AsUser(carol);
        SharingView carolView = (await carolClient.GetFromJsonAsync<SharingView>($"/api/v1/items/{file.Id}/sharing"))!;
        carolView.CanShare.ShouldBeFalse();
        carolView.Links.ShouldBeEmpty();
    }

    [Fact]
    public async Task Share_Validation_UnknownEmail404_Owner409_BadRole400()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "F");

        HttpResponseMessage unknown = await owner.PostAsJsonAsync($"/api/v1/items/{folder.Id}/permissions", new { email = "nobody@example.com", role = "viewer" });
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCode(unknown)).ShouldBe("DRIVE.USER_NOT_FOUND");

        Guid ownerId = (await GetItemAsync(owner, folder.Id)).OwnerId;
        HttpResponseMessage self = await owner.PostAsJsonAsync($"/api/v1/items/{folder.Id}/permissions", new { email = FakeUserDirectory.EmailOf(ownerId), role = "viewer" });
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCode(self)).ShouldBe("DRIVE.ALREADY_OWNER");

        (await owner.PostAsJsonAsync($"/api/v1/items/{folder.Id}/permissions", new { email = FakeUserDirectory.EmailOf(Guid.NewGuid()), role = "owner" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoke_RemovesAccessImmediately_AndChangeRoleApplies()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "F");
        Guid bob = Guid.NewGuid();
        using HttpClient bobClient = factory.CreateClient().AsUser(bob);
        PermissionView permission = await ShareAsync(owner, folder.Id, bob, "viewer");

        (await PatchAsync(bobClient, folder.Id, new { name = "G" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.PatchAsJsonAsync($"/api/v1/items/{folder.Id}/permissions/{permission.Id}", new { role = "editor" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PatchAsync(bobClient, folder.Id, new { name = "G" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await owner.DeleteAsync($"/api/v1/items/{folder.Id}/permissions/{permission.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await bobClient.GetAsync($"/api/v1/items/{folder.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.Sharing.Services.GetRequiredService<ITestHarness>().Published.Any<ShareRevoked>(
            x => x.Context.Message.PermissionId == permission.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Editor_UploadsIntoSharedFolder_OwnerPaysQuota_AndCannotMoveItOut()
    {
        using HttpClient owner = NewUser();
        Item ownerRoot = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, ownerRoot.Id, "Nhóm");
        Guid bob = Guid.NewGuid();
        using HttpClient bobClient = factory.CreateClient().AsUser(bob);
        Item bobRoot = await GetRootAsync(bobClient);
        await ShareAsync(owner, folder.Id, bob, "editor");

        Upload upload = await StartUploadAsync(bobClient, folder.Id, "báo cáo.pdf", 100);
        Item file = await CompleteAsync(bobClient, upload.UploadId);

        file.OwnerId.ShouldBe(folder.OwnerId);
        factory.Storage.SessionOwners[upload.UploadId].ShouldBe((folder.OwnerId, bob));
        (await ChildNamesAsync(owner, folder.Id)).ShouldBe(["báo cáo.pdf"]);

        HttpResponseMessage move = await bobClient.PostAsJsonAsync($"/api/v1/items/{file.Id}/move", new { newParentId = bobRoot.Id });
        move.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCode(move)).ShouldBe("DRIVE.INVALID_MOVE");

        // Sao chép thì được: bản sao thuộc Bob, Storage sao chép blob từ chủ sở hữu gốc.
        Item copy = await CopyAsync(bobClient, file.Id, new { targetParentId = bobRoot.Id });
        copy.OwnerId.ShouldBe(bob);
        factory.Storage.Copies.ShouldContain(c => c.UserId == bob);
    }

    [Fact]
    public async Task SharedSubtree_AncestorsStartAtSharedFolder_TrashedItemsDisappear()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item top = await CreateFolderAsync(owner, root.Id, "Top");
        Item shared = await CreateFolderAsync(owner, top.Id, "Shared");
        Item inner = await CreateFolderAsync(owner, shared.Id, "Inner");
        Item file = await UploadFileAsync(owner, inner.Id, "x.txt", 1);
        Guid bob = Guid.NewGuid();
        using HttpClient bobClient = factory.CreateClient().AsUser(bob);
        await ShareAsync(owner, shared.Id, bob, "viewer");

        Item[] ancestors = (await bobClient.GetFromJsonAsync<Item[]>($"/api/v1/items/{file.Id}/ancestors"))!;
        ancestors.Select(a => a.Name).ShouldBe(["Shared", "Inner"]); // không thấy "Top" và "My Drive" của chủ
        (await bobClient.GetAsync($"/api/v1/items/{top.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await PostAsync(owner, $"/api/v1/items/{inner.Id}/trash");
        (await bobClient.GetAsync($"/api/v1/items/{file.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ChildNamesAsync(bobClient, shared.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Link_AnonymousCanBrowseAndDownloadOnlyInsideLinkedFolder_UntilDisabled()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item outside = await UploadFileAsync(owner, root.Id, "riêng.txt", 1);
        Item folder = await CreateFolderAsync(owner, root.Id, "Công khai");
        Item sub = await CreateFolderAsync(owner, folder.Id, "Ảnh");
        Item file = await UploadFileAsync(owner, sub.Id, "a.png", 5);
        CreatedLink link = await CreateLinkAsync(owner, folder.Id, new { role = "viewer" });

        using HttpClient anonymous = factory.CreateClient();
        string baseUrl = $"/api/v1/public/links/{link.Token}";
        Item opened = (await anonymous.GetFromJsonAsync<Item>(baseUrl))!;
        opened.Id.ShouldBe(folder.Id);
        opened.Role.ShouldBe("viewer");
        (await anonymous.GetFromJsonAsync<Page>($"{baseUrl}/items/{sub.Id}/children"))!.Items.Select(i => i.Name).ShouldBe(["a.png"]);
        (await anonymous.GetFromJsonAsync<Item[]>($"{baseUrl}/items/{file.Id}/ancestors"))!.Select(a => a.Name).ShouldBe(["Công khai", "Ảnh"]);
        (await anonymous.PostAsJsonAsync($"{baseUrl}/items/{file.Id}/download-url", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await anonymous.GetAsync($"{baseUrl}/items/{outside.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/v1/items/{file.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/public/links/khong-ton-tai")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.DeleteAsync($"/api/v1/items/{folder.Id}/links/{link.Link.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.GetAsync(baseUrl)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"{baseUrl}/items/{file.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Link_WithPassword_RequiresHeader_AndEditorLinksAreRejected()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item file = await UploadFileAsync(owner, root.Id, "bí mật.txt", 1);

        (await owner.PostAsJsonAsync($"/api/v1/items/{file.Id}/links", new { role = "editor" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        CreatedLink link = await CreateLinkAsync(owner, file.Id, new { role = "viewer", password = "mat-khau-1" });
        link.Link.HasPassword.ShouldBeTrue();

        using HttpClient anonymous = factory.CreateClient();
        HttpResponseMessage missing = await anonymous.GetAsync($"/api/v1/public/links/{link.Token}");
        missing.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCode(missing)).ShouldBe("DRIVE.LINK_PASSWORD_REQUIRED");

        var wrong = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/links/{link.Token}");
        wrong.Headers.Add("X-Link-Password", "sai");
        (await anonymous.SendAsync(wrong)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var right = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/links/{link.Token}");
        right.Headers.Add("X-Link-Password", "mat-khau-1");
        HttpResponseMessage ok = await anonymous.SendAsync(right);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<Item>())!.Name.ShouldBe("bí mật.txt");
    }

    [Fact]
    public async Task Viewer_CannotCreateLinks_ButSeesPermissionList()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "F");
        Guid bob = Guid.NewGuid();
        await ShareAsync(owner, folder.Id, bob, "viewer");
        using HttpClient bobClient = factory.CreateClient().AsUser(bob);

        (await bobClient.PostAsJsonAsync($"/api/v1/items/{folder.Id}/links", new { role = "viewer" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bobClient.GetFromJsonAsync<SharingView>($"/api/v1/items/{folder.Id}/sharing"))!.Permissions.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ItemsPurged_RemovesPermissionsInSharing()
    {
        using HttpClient owner = NewUser();
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "F");
        await ShareAsync(owner, folder.Id, Guid.NewGuid(), "viewer");
        await CreateLinkAsync(owner, folder.Id, new { role = "viewer" });

        ITestHarness sharing = factory.Sharing.Services.GetRequiredService<ITestHarness>();
        await sharing.Bus.Publish(new ItemsPurged(folder.OwnerId, [folder.Id], []));
        for (int i = 0; i < 50 && await SharingCountAsync(folder.Id) > 0; i++)
        {
            await Task.Delay(100);
        }

        (await SharingCountAsync(folder.Id)).ShouldBe(0);
    }

    private async Task<int> SharingCountAsync(Guid itemId)
    {
        await using AsyncServiceScope scope = factory.Sharing.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SharingDbContext>();
        return await db.Permissions.CountAsync(p => p.ItemId == itemId) + await db.Links.CountAsync(l => l.ItemId == itemId);
    }

    private static async Task<PermissionView> ShareAsync(HttpClient owner, Guid itemId, Guid userId, string role)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/v1/items/{itemId}/permissions", new { email = FakeUserDirectory.EmailOf(userId), role, notify = false });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PermissionView>())!;
    }

    private static async Task<CreatedLink> CreateLinkAsync(HttpClient owner, Guid itemId, object body)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/v1/items/{itemId}/links", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedLink>())!;
    }
}
