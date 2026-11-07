using Grpc.Core;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Contracts.Storage.V1;
using Storage.Api.Data;
using Storage.Api.ObjectStorage;

namespace Storage.IntegrationTests;

// Phase 3: sao chép blob phía server, xoá blob khi Drive xoá vĩnh viễn / xoá version.
public sealed partial class StorageTests
{
    [Fact]
    public async Task CopyBlob_CopiesBytesServerSide_CountsQuota_AndIsLimitedToTheOwner()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);
        byte[] content = RandomBytes(4096);
        string source = await UploadAsync(grpc, user, content);

        CopyBlobResponse copy = await grpc.CopyBlobAsync(new CopyBlobRequest { UserId = user.ToString(), SourceBlobId = source });

        copy.BlobId.ShouldNotBe(source);
        copy.SizeBytes.ShouldBe(content.Length);
        (await QuotaAsync(user)).UsedBytes.ShouldBe(2 * content.Length);
        GetDownloadUrlResponse link = await grpc.GetDownloadUrlAsync(new GetDownloadUrlRequest { UserId = user.ToString(), BlobId = copy.BlobId, FileName = "copy.bin" });
        using HttpResponseMessage download = await _s3.GetAsync(link.Url);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(content);
        // Bản sao chờ Drive claim như một upload bình thường.
        (await WithDbAsync(db => db.Blobs.SingleAsync(b => b.Id == Guid.Parse(copy.BlobId)))).Status.ShouldBe(BlobStatus.Uploaded);

        Guid intruder = Guid.NewGuid();
        RpcException notOwner = await Should.ThrowAsync<RpcException>(() => factory.GrpcClientFor(intruder)
            .CopyBlobAsync(new CopyBlobRequest { UserId = intruder.ToString(), SourceBlobId = source }).ResponseAsync);
        notOwner.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Fact]
    public async Task CopyBlob_OverQuota_IsRejected_WithoutLeakingReservation()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);
        string source = await UploadAsync(grpc, user, RandomBytes((int)(StorageApiFactory.QuotaBytes * 2 / 3)));

        RpcException ex = await Should.ThrowAsync<RpcException>(() =>
            grpc.CopyBlobAsync(new CopyBlobRequest { UserId = user.ToString(), SourceBlobId = source }).ResponseAsync);

        ex.StatusCode.ShouldBe(StatusCode.ResourceExhausted);
        (await QuotaAsync(user)).ReservedBytes.ShouldBe(0);
    }

    [Fact]
    public async Task ItemsPurged_DeletesObjects_ReleasesQuota_AndIsIdempotent()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);
        string claimed = await UploadAsync(grpc, user, RandomBytes(300));
        string unclaimed = await UploadAsync(grpc, user, RandomBytes(200)); // event claim chưa tới — vẫn phải xoá được
        string kept = await UploadAsync(grpc, user, RandomBytes(100));
        Guid other = Guid.NewGuid();
        string othersBlob = await UploadAsync(factory.GrpcClientFor(other), other, RandomBytes(50));
        ITestHarness harness = factory.Services.GetRequiredService<ITestHarness>();
        await harness.Bus.Publish(new FileVersionAdded(Guid.NewGuid(), user, Guid.NewGuid(), 1, Guid.Parse(claimed), 300, "text/plain", 1, user));
        await WaitUntilAsync(async db => (await db.Blobs.SingleAsync(b => b.Id == Guid.Parse(claimed))).Status == BlobStatus.Claimed);

        var purged = new ItemsPurged(user, [Guid.NewGuid()], [Guid.Parse(claimed), Guid.Parse(unclaimed), Guid.Parse(othersBlob)]);
        await harness.Bus.Publish(purged);
        await WaitUntilAsync(async db => await db.Blobs.CountAsync(b => b.OwnerId == user && b.Status == BlobStatus.Deleted) == 2);
        await harness.Bus.Publish(purged with { EventId = Guid.NewGuid() }); // giao lại (id khác ⇒ không bị inbox chặn)
        // Không dùng Consumed.Any: nó có thể trả false ngay nếu harness coi bus là "rảnh" trước khi bản tin tới.
        for (int i = 0; i < 50 && !harness.Consumed.Select<ItemsPurged>(x => x.Context.Message.EventId != purged.EventId).Any(); i++)
        {
            await Task.Delay(100);
        }

        harness.Consumed.Select<ItemsPurged>(x => x.Context.Message.EventId != purged.EventId).ShouldNotBeEmpty();

        (await QuotaAsync(user)).UsedBytes.ShouldBe(100); // trả đúng một lần
        S3ObjectStorage s3 = factory.Services.GetRequiredService<S3ObjectStorage>();
        (await s3.GetSizeAsync(Blob.KeyFor(user, Guid.Parse(claimed)), CancellationToken.None)).ShouldBeNull();
        (await s3.GetSizeAsync(Blob.KeyFor(user, Guid.Parse(kept)), CancellationToken.None)).ShouldBe(100);
        // Blob của người khác trong event bị bỏ qua.
        (await WithDbAsync(db => db.Blobs.SingleAsync(b => b.Id == Guid.Parse(othersBlob)))).Status.ShouldBe(BlobStatus.Uploaded);
    }

    [Fact]
    public async Task FileVersionsDeleted_DeletesOldVersionBlob()
    {
        Guid user = Guid.NewGuid();
        var grpc = factory.GrpcClientFor(user);
        string old = await UploadAsync(grpc, user, RandomBytes(400));
        string current = await UploadAsync(grpc, user, RandomBytes(500));

        await factory.Services.GetRequiredService<ITestHarness>().Bus.Publish(
            new FileVersionsDeleted(Guid.NewGuid(), user, [Guid.NewGuid()], [Guid.Parse(old)], 3));
        await WaitUntilAsync(async db => (await db.Blobs.SingleAsync(b => b.Id == Guid.Parse(old))).Status == BlobStatus.Deleted);

        (await QuotaAsync(user)).UsedBytes.ShouldBe(500);
        (await WithDbAsync(db => db.Blobs.SingleAsync(b => b.Id == Guid.Parse(current)))).Status.ShouldBe(BlobStatus.Uploaded);
    }
}
