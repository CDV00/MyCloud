using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Grpc.Core;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Storage;
using MyCloud.Contracts.Storage.V1;
using MyCloud.Testing;
using Storage.Api.Archives;
using Storage.Api.Maintenance;
using Storage.Api.Messaging;

namespace Storage.IntegrationTests;

// Phase 5 (ADR-015): thumbnail của blob, tệp zip, chặn hiển thị inline nội dung có thể chạy script.
public sealed partial class StorageTests
{
    private sealed record ArchiveView(Guid Id, string Name, string Status, int FileCount, long? SizeBytes, int SkippedCount, string? Error);

    private sealed record UrlView(string Url, DateTimeOffset ExpiresAt);

    [Fact]
    public async Task Thumbnails_AreStoredPerBlob_SignedForServices_AndDeletedWithTheBlob()
    {
        Guid owner = Guid.NewGuid();
        string blobId = await UploadAsync(factory.GrpcClientFor(owner), owner, RandomBytes(500));
        byte[] webp = RandomBytes(300);

        PutThumbnailResponse put = await ServiceClient().PutThumbnailAsync(new PutThumbnailRequest
        {
            BlobId = blobId,
            Size = 256,
            Content = Google.Protobuf.ByteString.CopyFrom(webp),
        });
        put.Stored.ShouldBeTrue();

        var request = new GetThumbnailUrlsRequest { Size = 200 }; // làm tròn lên kích thước có sẵn (256)
        request.BlobIds.Add(blobId);
        string url = (await ServiceClient().GetThumbnailUrlsAsync(request)).Urls.ShouldHaveSingleItem();
        using (HttpResponseMessage image = await _s3.GetAsync(url))
        {
            image.StatusCode.ShouldBe(HttpStatusCode.OK);
            image.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
            image.Headers.CacheControl!.Private.ShouldBeTrue();
            (await image.Content.ReadAsByteArrayAsync()).ShouldBe(webp);
        }

        // Người dùng không tự ký / ghi thumbnail được (chỉ Drive / Media).
        RpcException denied = await Should.ThrowAsync<RpcException>(() => factory.GrpcClientFor(owner).GetThumbnailUrlsAsync(request).ResponseAsync);
        denied.StatusCode.ShouldBe(StatusCode.PermissionDenied);

        // Blob bị xoá ⇒ thumbnail bị xoá theo; thumbnail đến muộn không được lưu.
        await WithDbAsync(async db =>
        {
            await factory.Services.CreateScope().ServiceProvider.GetRequiredService<BlobReclaimer>()
                .DeleteAsync(owner, [Guid.Parse(blobId)], CancellationToken.None);
            return true;
        });
        (await _s3.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ServiceClient().PutThumbnailAsync(new PutThumbnailRequest
        {
            BlobId = blobId,
            Size = 1024,
            Content = Google.Protobuf.ByteString.CopyFrom(webp),
        })).Stored.ShouldBeFalse();
    }

    [Fact]
    public async Task Archive_IsBuiltInTheBackground_WithFoldersAndUnicodeNames_SkippingDeletedBlobs()
    {
        Guid user = Guid.NewGuid();
        byte[] first = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Xin chào MyCloud! ", 5000)));
        byte[] second = RandomBytes(300_000);
        string a = await UploadAsync(factory.GrpcClientFor(user), user, first);
        string b = await UploadAsync(factory.GrpcClientFor(user), user, second);

        var request = new CreateArchiveRequest { UserId = user.ToString(), Name = "Dự án" };
        request.Entries.Add(new ArchiveEntry { Path = "Dự án/" });
        request.Entries.Add(new ArchiveEntry { Path = "Dự án/Ghi chú.txt", BlobId = a, SizeBytes = first.Length, MimeType = "text/plain" });
        request.Entries.Add(new ArchiveEntry { Path = "Dự án/Ảnh/" });
        request.Entries.Add(new ArchiveEntry { Path = "Dự án/Ảnh/a.jpg", BlobId = b, SizeBytes = second.Length, MimeType = "image/jpeg" });
        request.Entries.Add(new ArchiveEntry { Path = "Dự án/đã xoá.bin", BlobId = Guid.NewGuid().ToString(), SizeBytes = 1 });
        string archiveId = (await ServiceClient().CreateArchiveAsync(request)).ArchiveId;

        using HttpClient rest = RestFor(user);
        ArchiveView view = await WaitForArchiveAsync(rest, archiveId);
        view.Status.ShouldBe("ready");
        view.Name.ShouldBe("Dự án.zip");
        view.SkippedCount.ShouldBe(1);
        (await factory.Services.GetRequiredService<ITestHarness>().Published.Any<ArchiveReady>(x =>
            x.Context.Message.ArchiveId == Guid.Parse(archiveId) && x.Context.Message.FileCount == 2)).ShouldBeTrue();

        HttpResponseMessage link = await rest.PostAsync($"/api/v1/archives/{archiveId}/download-url", null);
        link.EnsureSuccessStatusCode();
        using HttpResponseMessage download = await _s3.GetAsync((await link.Content.ReadFromJsonAsync<UrlView>())!.Url);
        string disposition = string.Join(",", download.Content.Headers.GetValues("Content-Disposition"));
        disposition.ShouldStartWith("attachment;", customMessage: disposition);
        disposition.ShouldContain("filename*=UTF-8''D%E1%BB%B1%20%C3%A1n.zip", customMessage: disposition);
        byte[] zipBytes = await download.Content.ReadAsByteArrayAsync();
        zipBytes.Length.ShouldBe((int)view.SizeBytes!.Value);

        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).ShouldBe(["Dự án/", "Dự án/Ghi chú.txt", "Dự án/Ảnh/", "Dự án/Ảnh/a.jpg"]);
        (await ReadAllAsync(zip.GetEntry("Dự án/Ghi chú.txt")!)).ShouldBe(first);
        (await ReadAllAsync(zip.GetEntry("Dự án/Ảnh/a.jpg")!)).ShouldBe(second);
        zip.GetEntry("Dự án/Ghi chú.txt")!.CompressedLength.ShouldBeLessThan(first.Length); // văn bản được nén

        // Người khác không thấy / không tải được.
        using HttpClient stranger = factory.CreateClient().AsUser(Guid.NewGuid());
        (await stranger.GetAsync($"/api/v1/archives/{archiveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archive_ExpiresAfterTtl_AndTheObjectIsDeleted()
    {
        Guid user = Guid.NewGuid();
        string blob = await UploadAsync(factory.GrpcClientFor(user), user, RandomBytes(100));
        var request = new CreateArchiveRequest { UserId = user.ToString(), Name = "x.zip" };
        request.Entries.Add(new ArchiveEntry { Path = "x.bin", BlobId = blob, SizeBytes = 100 });
        string archiveId = (await ServiceClient().CreateArchiveAsync(request)).ArchiveId;
        using HttpClient rest = RestFor(user);
        (await WaitForArchiveAsync(rest, archiveId)).Status.ShouldBe("ready");
        string url = (await (await rest.PostAsync($"/api/v1/archives/{archiveId}/download-url", null)).Content.ReadFromJsonAsync<UrlView>())!.Url;

        await WithDbAsync(db => db.Archives.Where(x => x.Id == Guid.Parse(archiveId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await factory.Services.GetRequiredService<StorageMaintenance>().RunOnceAsync(CancellationToken.None);

        (await rest.GetFromJsonAsync<ArchiveView>($"/api/v1/archives/{archiveId}"))!.Status.ShouldBe("expired");
        (await rest.PostAsync($"/api/v1/archives/{archiveId}/download-url", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _s3.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateArchive_RejectsUsersAndUnsafePaths()
    {
        Guid user = Guid.NewGuid();
        var request = new CreateArchiveRequest { UserId = user.ToString(), Name = "x" };
        request.Entries.Add(new ArchiveEntry { Path = "a/../../etc/passwd", BlobId = Guid.NewGuid().ToString(), SizeBytes = 1 });

        (await Should.ThrowAsync<RpcException>(() => factory.GrpcClientFor(user).CreateArchiveAsync(request).ResponseAsync))
            .StatusCode.ShouldBe(StatusCode.PermissionDenied);
        (await Should.ThrowAsync<RpcException>(() => ServiceClient().CreateArchiveAsync(request).ResponseAsync))
            .StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Theory]
    [InlineData("text/html", "text/plain; charset=utf-8", "inline")]
    [InlineData("image/svg+xml", "text/plain; charset=utf-8", "inline")]
    [InlineData("image/png", "image/png", "inline")]
    [InlineData("application/pdf", "application/pdf", "inline")]
    [InlineData("application/x-msdownload", "application/octet-stream", "attachment")]
    public async Task InlineDownload_NeverServesContentThatCouldRunScript(string mimeType, string served, string disposition)
    {
        Guid user = Guid.NewGuid();
        StorageService.StorageServiceClient grpc = factory.GrpcClientFor(user);
        byte[] content = Encoding.UTF8.GetBytes("<script>alert(1)</script>");
        CreateUploadSessionResponse session = await grpc.CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = content.Length, MimeType = mimeType });
        string blobId = (await grpc.CompleteUploadAsync(CompleteRequest(user, session, await PutPartsAsync(session, content)))).BlobId;

        GetDownloadUrlResponse link = await grpc.GetDownloadUrlAsync(new GetDownloadUrlRequest
        {
            UserId = user.ToString(),
            BlobId = blobId,
            FileName = "x",
            MimeType = mimeType,
            Inline = true,
        });

        using HttpResponseMessage response = await _s3.GetAsync(link.Url);
        response.Content.Headers.ContentType!.ToString().ShouldBe(served);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe(disposition);
    }

    [Fact]
    public async Task InternalDownloadUrls_AreOnlyForServices()
    {
        Guid user = Guid.NewGuid();
        string blobId = await UploadAsync(factory.GrpcClientFor(user), user, RandomBytes(10));
        var request = new GetDownloadUrlRequest { UserId = user.ToString(), BlobId = blobId, FileName = "x", Internal = true };

        (await Should.ThrowAsync<RpcException>(() => factory.GrpcClientFor(user).GetDownloadUrlAsync(request).ResponseAsync))
            .StatusCode.ShouldBe(StatusCode.PermissionDenied);
        string url = (await ServiceClient().GetDownloadUrlAsync(request)).Url;
        (await _s3.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>REST qua TestServer: URL tải được ký theo origin của request ⇒ giả lập Gateway trỏ origin về MinIO.</summary>
    private HttpClient RestFor(Guid user)
    {
        HttpClient client = factory.CreateClient().AsUser(user);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", factory.MinioEndpoint.Authority);
        return client;
    }

    private static async Task<ArchiveView> WaitForArchiveAsync(HttpClient rest, string archiveId)
    {
        for (int i = 0; i < 100; i++)
        {
            ArchiveView view = (await rest.GetFromJsonAsync<ArchiveView>($"/api/v1/archives/{archiveId}"))!;
            if (view.Status is not ("pending" or "building"))
            {
                return view;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Archive was not built within 10 seconds.");
    }

    private static async Task<byte[]> ReadAllAsync(ZipArchiveEntry entry)
    {
        await using Stream stream = await entry.OpenAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
