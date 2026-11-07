using System.Collections.Concurrent;
using System.Net;
using Grpc.Core;
using MassTransit;
using MassTransit.Testing;
using Media.Worker.Thumbnails;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Contracts.Events.Media;
using MyCloud.Contracts.Storage.V1;
using SkiaSharp;

namespace Media.Worker.Tests;

/// <summary>Consumer chạy qua MassTransit test harness; Storage (gRPC) và object storage (HTTP) giả trong bộ nhớ.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Giải phóng trong DisposeAsync (IAsyncLifetime).")]
public sealed class MediaThumbnailConsumerTests : IAsyncLifetime
{
    private readonly FakeStorage _storage = new();
    private readonly FakeObjectStore _objects = new();
    private ServiceProvider _provider = null!;

    private ITestHarness Harness => _provider.GetRequiredService<ITestHarness>();

    public async Task InitializeAsync()
    {
        var options = new MediaOptions { FfmpegPath = "ffmpeg-khong-ton-tai" };
        _provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(options)
            .AddSingleton<VideoFrameExtractor>()
            .AddSingleton<StorageService.StorageServiceClient>(_storage)
            .AddMassTransitTestHarness(x => x.AddConsumer<MediaThumbnailConsumer, MediaThumbnailConsumerDefinition>())
            .AddHttpClient(MediaThumbnailConsumer.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _objects).Services
            .BuildServiceProvider(true);
        await Harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        _objects.Dispose();
    }

    [Fact]
    public async Task Image_GetsBothThumbnailSizes_ThenThumbnailGeneratedIsPublished()
    {
        FileVersionAdded e = Version("image/png", ThumbnailRendererTests.Png(1600, 1200, SKColors.Green));

        await ConsumeAsync(e);

        _storage.Thumbnails.Keys.ShouldBe([(e.BlobId, 256), (e.BlobId, 1024)], ignoreOrder: true);
        _storage.InternalRequests.ShouldAllBe(r => r.Internal && r.UserId == e.OwnerId.ToString());
        (await Harness.Published.Any<ThumbnailGenerated>(x =>
            x.Context.Message.ItemId == e.ItemId && x.Context.Message.VersionId == e.VersionId && x.Context.Message.Sizes.Count == 2)).ShouldBeTrue();
    }

    [Fact]
    public async Task Pdf_GetsAThumbnailOfItsFirstPage()
    {
        FileVersionAdded e = Version("application/pdf", ThumbnailRendererTests.MinimalPdf(200, 400));

        await ConsumeAsync(e);

        _storage.Thumbnails.Keys.ShouldContain((e.BlobId, 1024));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("video/mp4")] // không có ffmpeg ⇒ bỏ qua video
    public async Task UnsupportedContent_IsSkipped_WithoutCallingStorage(string mime)
    {
        FileVersionAdded e = Version(mime, [1, 2, 3]);

        await ConsumeAsync(e);

        _storage.InternalRequests.ShouldBeEmpty();
        (await Harness.Published.Any<ThumbnailGenerated>(x => x.Context.Message.ItemId == e.ItemId)).ShouldBeFalse();
    }

    [Fact]
    public async Task CorruptImage_IsSkipped_WithoutFaultingTheMessage()
    {
        FileVersionAdded e = Version("image/jpeg", "đây không phải ảnh"u8.ToArray());

        await ConsumeAsync(e);

        _storage.Thumbnails.ShouldBeEmpty();
        (await Harness.Published.Any<Fault<FileVersionAdded>>()).ShouldBeFalse();
    }

    [Fact]
    public async Task DeletedBlob_StopsWithoutPublishing()
    {
        FileVersionAdded e = Version("image/png", ThumbnailRendererTests.Png(10, 10, SKColors.Black));
        _storage.Deleted.Add(e.BlobId);

        await ConsumeAsync(e);

        (await Harness.Published.Any<ThumbnailGenerated>(x => x.Context.Message.ItemId == e.ItemId)).ShouldBeFalse();
    }

    private FileVersionAdded Version(string mime, byte[] content)
    {
        var e = new FileVersionAdded(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), content.Length, mime, 1, Guid.NewGuid());
        _objects.Content[$"http://minio/{e.BlobId:N}"] = content;
        return e;
    }

    private async Task ConsumeAsync(FileVersionAdded e)
    {
        await Harness.Bus.Publish(e);
        (await Harness.Consumed.Any<FileVersionAdded>(x => x.Context.Message.EventId == e.EventId)).ShouldBeTrue();
        await Harness.InactivityTask; // đợi consumer chạy xong và mọi bản tin đã publish
    }

    private sealed class FakeStorage : StorageService.StorageServiceClient
    {
        public ConcurrentDictionary<(Guid BlobId, int Size), byte[]> Thumbnails { get; } = new();

        public ConcurrentBag<Guid> Deleted { get; } = [];

        public ConcurrentQueue<GetDownloadUrlRequest> InternalRequests { get; } = new();

        public override AsyncUnaryCall<GetDownloadUrlResponse> GetDownloadUrlAsync(GetDownloadUrlRequest request, CallOptions options)
        {
            InternalRequests.Enqueue(request);
            return Call(new GetDownloadUrlResponse { Url = $"http://minio/{Guid.Parse(request.BlobId):N}" });
        }

        public override AsyncUnaryCall<PutThumbnailResponse> PutThumbnailAsync(PutThumbnailRequest request, CallOptions options)
        {
            Guid blobId = Guid.Parse(request.BlobId);
            if (Deleted.Contains(blobId))
            {
                return Call(new PutThumbnailResponse { Stored = false });
            }

            Thumbnails[(blobId, request.Size)] = request.Content.ToByteArray();
            return Call(new PutThumbnailResponse { Stored = true });
        }

        private static AsyncUnaryCall<T> Call<T>(T response) =>
            new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    private sealed class FakeObjectStore : HttpMessageHandler
    {
        public ConcurrentDictionary<string, byte[]> Content { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Content.TryGetValue(request.RequestUri!.ToString(), out byte[]? bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
