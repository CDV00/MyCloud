using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Contracts.Storage.V1;
using MyCloud.Testing;
using Storage.Api.Data;
using Storage.Api.Maintenance;

namespace Storage.IntegrationTests;

public sealed class StorageTests(StorageApiFactory factory) : IClassFixture<StorageApiFactory>, IDisposable
{
    private sealed record QuotaResponse(long LimitBytes, long UsedBytes, long ReservedBytes, long AvailableBytes);

    private sealed record PartUrl(int PartNumber, string Url);

    private sealed record UploadedPart(int PartNumber, string ETag, long Size);

    // Upload thẳng lên MinIO bằng presigned URL — đúng như trình duyệt làm (không qua service).
    private readonly HttpClient _s3 = new();

    public void Dispose() => _s3.Dispose();

    [Fact]
    public async Task MultipartUpload_ThroughPresignedUrls_ThenDownload_RoundTripsBytes_AndCountsQuota()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);
        byte[] content = RandomBytes((int)StorageApiFactory.PartSizeBytes + 1_000_000); // 2 part

        CreateUploadSessionResponse session = await grpc.CreateUploadSessionAsync(new CreateUploadSessionRequest
        {
            UserId = user.ToString(),
            SizeBytes = content.Length,
            MimeType = "application/pdf",
        });
        session.PartCount.ShouldBe(2);
        session.Parts.Count.ShouldBe(2);
        (await QuotaAsync(user)).ReservedBytes.ShouldBe(content.Length);

        var completed = await grpc.CompleteUploadAsync(CompleteRequest(user, session, await PutPartsAsync(session, content)));

        completed.SizeBytes.ShouldBe(content.Length);
        QuotaResponse quota = await QuotaAsync(user);
        quota.UsedBytes.ShouldBe(content.Length);
        quota.ReservedBytes.ShouldBe(0);

        GetDownloadUrlResponse link = await grpc.GetDownloadUrlAsync(new GetDownloadUrlRequest
        {
            UserId = user.ToString(),
            BlobId = completed.BlobId,
            FileName = "báo cáo.pdf",
        });
        using HttpResponseMessage download = await _s3.GetAsync(link.Url);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(content);
        download.Content.Headers.ContentDisposition!.ToString().ShouldContain("filename*=UTF-8''b%C3%A1o%20c%C3%A1o.pdf");

        // Hoàn tất lặp lại (mạng chập chờn) trả cùng kết quả, không tính quota hai lần.
        (await grpc.CompleteUploadAsync(CompleteRequest(user, session, []))).BlobId.ShouldBe(completed.BlobId);
        (await QuotaAsync(user)).UsedBytes.ShouldBe(content.Length);
    }

    [Fact]
    public async Task CreateUploadSession_OverQuota_IsResourceExhausted_WithErrorCode()
    {
        Guid user = Guid.NewGuid();

        RpcException ex = await Should.ThrowAsync<RpcException>(() => factory.GrpcClientFor(user).CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = StorageApiFactory.QuotaBytes + 1 }).ResponseAsync);

        ex.StatusCode.ShouldBe(StatusCode.ResourceExhausted);
        ex.Trailers.GetValue("x-error-code").ShouldBe("STORAGE.QUOTA_EXCEEDED");
        (await QuotaAsync(user)).ReservedBytes.ShouldBe(0);
    }

    [Fact]
    public async Task Complete_WithFewerBytesThanDeclared_IsRejected_AndReleasesReservation()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);
        CreateUploadSessionResponse session = await grpc.CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = 1000 });

        IReadOnlyList<CompletedPart> parts = await PutPartsAsync(session, RandomBytes(10));
        RpcException ex = await Should.ThrowAsync<RpcException>(() => grpc.CompleteUploadAsync(CompleteRequest(user, session, parts)).ResponseAsync);

        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
        ex.Trailers.GetValue("x-error-code").ShouldBe("STORAGE.SIZE_MISMATCH");
        QuotaResponse quota = await QuotaAsync(user);
        quota.ReservedBytes.ShouldBe(0);
        quota.UsedBytes.ShouldBe(0);
    }

    [Fact]
    public async Task Abort_ReleasesReservation()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);
        CreateUploadSessionResponse session = await grpc.CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = 4096 });

        await grpc.AbortUploadAsync(new AbortUploadRequest { UserId = user.ToString(), SessionId = session.SessionId });

        (await QuotaAsync(user)).ReservedBytes.ShouldBe(0);
    }

    [Fact]
    public async Task Rest_ListParts_ShowsUploadedParts_ForResume_AndSignsMoreUrls()
    {
        Guid user = Guid.NewGuid();
        CreateUploadSessionResponse session = await factory.GrpcClientFor(user).CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = StorageApiFactory.PartSizeBytes * 2 });
        using HttpClient rest = factory.CreateClient().AsUser(user);

        // Chỉ upload part 1, rồi "tải lại trang": hỏi part nào đã có để làm tiếp.
        await PutAsync(session.Parts[0].Url, RandomBytes((int)StorageApiFactory.PartSizeBytes));
        UploadedPart[] uploaded = (await rest.GetFromJsonAsync<UploadedPart[]>($"/api/v1/uploads/{session.SessionId}/parts"))!;
        var sign = await rest.PostAsJsonAsync($"/api/v1/uploads/{session.SessionId}/part-urls", new { partNumbers = new[] { 2 } });

        uploaded.ShouldHaveSingleItem().PartNumber.ShouldBe(1);
        sign.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await sign.Content.ReadFromJsonAsync<PartUrl[]>())!.ShouldHaveSingleItem().PartNumber.ShouldBe(2);
    }

    [Fact]
    public async Task PartUrls_AreSignedForThePublicOriginOfTheBrowser()
    {
        Guid user = Guid.NewGuid();

        CreateUploadSessionResponse session = await factory.GrpcClientFor(user).CreateUploadSessionAsync(new CreateUploadSessionRequest
        {
            UserId = user.ToString(),
            SizeBytes = 10,
            PublicBaseUrl = "http://mycloud.localhost:8081",
        });

        session.Parts.ShouldHaveSingleItem().Url.ShouldStartWith("http://mycloud.localhost:8081/mycloud-test/u/");
    }

    [Fact]
    public async Task OtherUser_CannotUseSession_OrImpersonate()
    {
        Guid owner = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        CreateUploadSessionResponse session = await factory.GrpcClientFor(owner).CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = owner.ToString(), SizeBytes = 10 });
        using HttpClient rest = factory.CreateClient().AsUser(other);

        (await rest.GetAsync($"/api/v1/uploads/{session.SessionId}/parts")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await rest.PostAsJsonAsync($"/api/v1/uploads/{session.SessionId}/part-urls", new { partNumbers = new[] { 1 } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Token của "other" nhưng khai user_id là chủ sở hữu ⇒ bị chặn.
        RpcException ex = await Should.ThrowAsync<RpcException>(() => factory.GrpcClientFor(other).AbortUploadAsync(
            new AbortUploadRequest { UserId = owner.ToString(), SessionId = session.SessionId }).ResponseAsync);
        ex.StatusCode.ShouldBe(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task Rest_WithoutToken_Returns401()
    {
        using HttpClient anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/quota")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Maintenance_ExpiresStaleUploads_AndDeletesUnclaimedBlobs_ButKeepsClaimedOnes()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);

        CreateUploadSessionResponse stale = await grpc.CreateUploadSessionAsync(new CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = 100 });
        string unclaimed = await UploadAsync(grpc, user, RandomBytes(200));
        string claimed = await UploadAsync(grpc, user, RandomBytes(300));

        // Drive ghi nhận blob "claimed" (FileVersionAdded) ⇒ không bao giờ bị GC.
        await factory.Services.GetRequiredService<ITestHarness>().Bus.Publish(new FileVersionAdded(
            Guid.NewGuid(), user, Guid.NewGuid(), 1, Guid.Parse(claimed), 300, "application/octet-stream", 1, user));
        await WaitUntilAsync(async db => (await db.Blobs.SingleAsync(b => b.Id == Guid.Parse(claimed))).Status == BlobStatus.Claimed);

        // Giả lập thời gian trôi qua 2 ngày.
        await WithDbAsync(db => db.Blobs.Where(b => b.OwnerId == user).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-2))
            .SetProperty(b => b.UploadedAt, b => b.UploadedAt == null ? null : DateTimeOffset.UtcNow.AddDays(-2))));

        await factory.Services.GetRequiredService<StorageMaintenance>().RunOnceAsync(CancellationToken.None);

        await WithDbAsync(async db =>
        {
            (await db.Blobs.SingleAsync(b => b.Id == Guid.Parse(stale.BlobId))).Status.ShouldBe(BlobStatus.Aborted);
            (await db.Blobs.SingleAsync(b => b.Id == Guid.Parse(unclaimed))).Status.ShouldBe(BlobStatus.Deleted);
            (await db.Blobs.SingleAsync(b => b.Id == Guid.Parse(claimed))).Status.ShouldBe(BlobStatus.Claimed);
            return 0;
        });
        QuotaResponse quota = await QuotaAsync(user);
        quota.ReservedBytes.ShouldBe(0);
        quota.UsedBytes.ShouldBe(300); // chỉ còn blob đã được claim
    }

    private async Task<string> UploadAsync(StorageService.StorageServiceClient grpc, Guid user, byte[] content)
    {
        CreateUploadSessionResponse session = await grpc.CreateUploadSessionAsync(
            new CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = content.Length });
        return (await grpc.CompleteUploadAsync(CompleteRequest(user, session, await PutPartsAsync(session, content)))).BlobId;
    }

    private async Task<IReadOnlyList<CompletedPart>> PutPartsAsync(CreateUploadSessionResponse session, byte[] content)
    {
        var parts = new List<CompletedPart>();
        foreach (PresignedPart part in session.Parts)
        {
            int offset = (int)((part.PartNumber - 1) * session.PartSize);
            int length = (int)Math.Min(session.PartSize, content.Length - offset);
            string etag = await PutAsync(part.Url, content.AsSpan(offset, Math.Max(0, length)).ToArray());
            parts.Add(new CompletedPart { PartNumber = part.PartNumber, Etag = etag });
        }

        return parts;
    }

    private async Task<string> PutAsync(string url, byte[] bytes)
    {
        using var body = new ByteArrayContent(bytes);
        using HttpResponseMessage response = await _s3.PutAsync(url, body);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private static CompleteUploadRequest CompleteRequest(Guid user, CreateUploadSessionResponse session, IEnumerable<CompletedPart> parts)
    {
        var request = new CompleteUploadRequest { UserId = user.ToString(), SessionId = session.SessionId };
        request.Parts.AddRange(parts);
        return request;
    }

    private async Task<QuotaResponse> QuotaAsync(Guid user)
    {
        using HttpClient rest = factory.CreateClient().AsUser(user);
        return (await rest.GetFromJsonAsync<QuotaResponse>("/api/v1/quota"))!;
    }

    private async Task<T> WithDbAsync<T>(Func<StorageDbContext, Task<T>> work)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<StorageDbContext>());
    }

    private async Task WaitUntilAsync(Func<StorageDbContext, Task<bool>> condition)
    {
        for (int i = 0; i < 50; i++)
        {
            if (await WithDbAsync(condition))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condition was not met within 5 seconds.");
    }

    private static byte[] RandomBytes(int length)
    {
        byte[] bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }
}
