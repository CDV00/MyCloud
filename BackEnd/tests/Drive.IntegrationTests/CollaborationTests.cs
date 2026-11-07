using System.Net;
using System.Net.Http.Json;
using MassTransit.Testing;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Testing;

namespace Drive.IntegrationTests;

// Lịch sử & trao đổi của tệp (ADR-017): ghi chú phiên bản, tin nhắn / góp ý phiên bản, quyền, người nhận thông báo.
public sealed partial class DriveApiTests
{
    private sealed record CommentView(
        Guid Id, Guid? VersionId, int? VersionNo, Guid? ParentId, Guid AuthorId, string? Body, Guid[] Mentions, bool Deleted,
        DateTimeOffset? ResolvedAt, DateTimeOffset? EditedAt);

    private sealed record NoteVersion(Guid Id, int VersionNo, Guid CreatedBy, string Source, string? Note);

    private sealed record Ticket(string Token, DateTimeOffset ExpiresAt);

    [Fact]
    public async Task VersionNotes_AreSavedWithTheVersion_AndOnlyTheAuthorOrOwnerCanChangeThem()
    {
        Guid ownerId = Guid.NewGuid(), editorId = Guid.NewGuid();
        using HttpClient owner = factory.CreateClient().AsUser(ownerId);
        using HttpClient editor = factory.CreateClient().AsUser(editorId);
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "Chung");
        await ShareAsync(owner, folder.Id, editorId, "editor");
        Item file = await UploadFileAsync(owner, folder.Id, "kế hoạch.txt", 10);

        HttpResponseMessage started = await editor.PostAsJsonAsync("/api/v1/uploads", new
        {
            targetItemId = file.Id,
            sizeBytes = 12,
            mimeType = "text/plain",
            source = "edit",
            note = "  Sửa số liệu quý 3  ",
        });
        started.EnsureSuccessStatusCode();
        await CompleteAsync(editor, (await started.Content.ReadFromJsonAsync<Upload>())!.UploadId);

        NoteVersion[] versions = (await owner.GetFromJsonAsync<NoteVersion[]>($"/api/v1/items/{file.Id}/versions"))!;
        NoteVersion v2 = versions.Single(v => v.VersionNo == 2);
        (v2.CreatedBy, v2.Source, v2.Note).ShouldBe((editorId, "edit", "Sửa số liệu quý 3"));

        // Người tạo phiên bản 1 là chủ sở hữu: người chỉnh sửa không sửa được ghi chú của bản đó.
        Guid v1 = versions.Single(v => v.VersionNo == 1).Id;
        (await editor.PatchAsJsonAsync($"/api/v1/items/{file.Id}/versions/{v1}", new { note = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.PatchAsJsonAsync($"/api/v1/items/{file.Id}/versions/{v2.Id}", new { note = "Sửa số liệu Q3" })).EnsureSuccessStatusCode();
        (await owner.PatchAsJsonAsync($"/api/v1/items/{file.Id}/versions/{v1}", new { note = "Bản đầu" })).EnsureSuccessStatusCode();

        // Khôi phục kèm lý do.
        (await owner.PostAsJsonAsync($"/api/v1/items/{file.Id}/versions/{v1}/restore", new { note = "Quay lại bản đầu" })).EnsureSuccessStatusCode();
        versions = (await owner.GetFromJsonAsync<NoteVersion[]>($"/api/v1/items/{file.Id}/versions"))!;
        versions.Select(v => (v.VersionNo, v.Note)).ShouldBe([(3, "Quay lại bản đầu"), (2, "Sửa số liệu Q3"), (1, "Bản đầu")]);
        (await Harness.Published.Any<ItemHistoryChanged>(x => x.Context.Message.ItemId == file.Id && x.Context.Message.Kind == "version_note"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Comments_ViewersRead_CommentersWrite_AndRecipientsAreTheRightPeople()
    {
        Guid ownerId = Guid.NewGuid(), editorId = Guid.NewGuid(), commenterId = Guid.NewGuid(), viewerId = Guid.NewGuid(), strangerId = Guid.NewGuid();
        using HttpClient owner = factory.CreateClient().AsUser(ownerId);
        using HttpClient editor = factory.CreateClient().AsUser(editorId);
        using HttpClient commenter = factory.CreateClient().AsUser(commenterId);
        using HttpClient viewer = factory.CreateClient().AsUser(viewerId);
        using HttpClient stranger = factory.CreateClient().AsUser(strangerId);
        Item root = await GetRootAsync(owner);
        Item folder = await CreateFolderAsync(owner, root.Id, "Dự án");
        await ShareAsync(owner, folder.Id, editorId, "editor");
        await ShareAsync(owner, folder.Id, commenterId, "commenter");
        await ShareAsync(owner, folder.Id, viewerId, "viewer");
        Item file = await UploadFileAsync(owner, folder.Id, "báo cáo.txt", 10);
        HttpResponseMessage started = await editor.PostAsJsonAsync("/api/v1/uploads", new { targetItemId = file.Id, sizeBytes = 11, source = "edit" });
        started.EnsureSuccessStatusCode();
        await CompleteAsync(editor, (await started.Content.ReadFromJsonAsync<Upload>())!.UploadId);
        Guid v2 = (await owner.GetFromJsonAsync<NoteVersion[]>($"/api/v1/items/{file.Id}/versions"))!.Single(v => v.VersionNo == 2).Id;

        // Người nhận xét góp ý phiên bản 2 (của người chỉnh sửa) và nhắc tên một người không có quyền (bị bỏ qua).
        HttpResponseMessage posted = await commenter.PostAsJsonAsync($"/api/v1/items/{file.Id}/comments", new
        {
            body = "Số liệu dòng 3 chưa đúng?",
            versionId = v2,
            mentions = new[] { viewerId, strangerId },
        });
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        CommentView thread = (await posted.Content.ReadFromJsonAsync<CommentView>())!;
        thread.VersionNo.ShouldBe(2);
        thread.Mentions.ShouldBe([viewerId]);
        (await Harness.Published.Any<ItemCommentPosted>(x => x.Context.Message.CommentId == thread.Id
            && x.Context.Message.Recipients.OrderBy(r => r).SequenceEqual(new[] { ownerId, editorId, viewerId }.OrderBy(r => r))
            && x.Context.Message.VersionNo == 2)).ShouldBeTrue();

        // Người chỉnh sửa trả lời; trả lời của trả lời vẫn thuộc luồng gốc và cùng phiên bản.
        HttpResponseMessage reply = await editor.PostAsJsonAsync($"/api/v1/items/{file.Id}/comments", new { body = "Đã sửa, xem lại giúp", parentId = thread.Id });
        CommentView replyView = (await reply.Content.ReadFromJsonAsync<CommentView>())!;
        CommentView nested = (await (await owner.PostAsJsonAsync($"/api/v1/items/{file.Id}/comments", new { body = "OK", parentId = replyView.Id }))
            .Content.ReadFromJsonAsync<CommentView>())!;
        (nested.ParentId, nested.VersionId).ShouldBe((thread.Id, v2));
        (await Harness.Published.Any<ItemCommentPosted>(x => x.Context.Message.CommentId == nested.Id
            && x.Context.Message.Recipients.OrderBy(r => r).SequenceEqual(new[] { commenterId, editorId }.OrderBy(r => r)))).ShouldBeTrue();

        // Người xem đọc được nhưng không viết được; người ngoài không thấy gì.
        (await viewer.GetFromJsonAsync<CommentView[]>($"/api/v1/items/{file.Id}/comments"))!.Length.ShouldBe(3);
        (await viewer.PostAsJsonAsync($"/api/v1/items/{file.Id}/comments", new { body = "hi" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/v1/items/{file.Id}/comments")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Sửa: chỉ người viết. Xoá: người viết hoặc chủ tệp. Đánh dấu đã xử lý: luồng gốc.
        (await owner.PatchAsJsonAsync($"/api/v1/items/{file.Id}/comments/{thread.Id}", new { body = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await commenter.PatchAsJsonAsync($"/api/v1/items/{file.Id}/comments/{thread.Id}", new { body = "Số liệu dòng 3 sai" })).EnsureSuccessStatusCode();
        (await editor.DeleteAsync($"/api/v1/items/{file.Id}/comments/{thread.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/items/{file.Id}/comments/{replyView.Id}")).EnsureSuccessStatusCode();
        (await commenter.PostAsJsonAsync($"/api/v1/items/{file.Id}/comments/{replyView.Id}/resolve", new { resolved = true }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await commenter.PostAsJsonAsync($"/api/v1/items/{file.Id}/comments/{thread.Id}/resolve", new { resolved = true })).EnsureSuccessStatusCode();

        CommentView[] all = (await owner.GetFromJsonAsync<CommentView[]>($"/api/v1/items/{file.Id}/comments"))!;
        CommentView root1 = all.Single(c => c.Id == thread.Id);
        (root1.Body, root1.EditedAt is not null, root1.ResolvedAt is not null).ShouldBe(("Số liệu dòng 3 sai", true, true));
        CommentView deleted = all.Single(c => c.Id == replyView.Id);
        (deleted.Deleted, deleted.Body).ShouldBe((true, null));
    }

    [Fact]
    public async Task LiveTicket_IsIssuedOnlyToPeopleWhoCanView_AndBoundToThem()
    {
        Guid ownerId = Guid.NewGuid();
        using HttpClient owner = factory.CreateClient().AsUser(ownerId);
        using HttpClient stranger = NewUser();
        Item root = await GetRootAsync(owner);
        Item file = await UploadFileAsync(owner, root.Id, "a.txt", 1);

        HttpResponseMessage response = await owner.PostAsync($"/api/v1/items/{file.Id}/live", null);
        Ticket ticket = (await response.Content.ReadFromJsonAsync<Ticket>())!;
        LiveRoomToken.Verify(DriveApiFactory.CollaborationSecret, ticket.Token, ownerId, DateTimeOffset.UtcNow).ShouldBe(file.Id);
        LiveRoomToken.Verify(DriveApiFactory.CollaborationSecret, ticket.Token, Guid.NewGuid(), DateTimeOffset.UtcNow).ShouldBeNull();
        LiveRoomToken.Verify("other-secret", ticket.Token, ownerId, DateTimeOffset.UtcNow).ShouldBeNull();
        LiveRoomToken.Verify(DriveApiFactory.CollaborationSecret, ticket.Token, ownerId, ticket.ExpiresAt.AddMinutes(1)).ShouldBeNull();

        (await stranger.PostAsync($"/api/v1/items/{file.Id}/live", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
