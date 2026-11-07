using MassTransit;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Contracts.Events.Media;
using MyCloud.Contracts.Storage.V1;
using SkiaSharp;

namespace Media.Worker.Thumbnails;

/// <summary>
/// Nội dung mới của file ⇒ thumbnail WebP 256 px và 1024 px (docs/05 §8, ADR-015). Đọc blob qua presigned URL nội bộ
/// mà Storage ký cho Media (định danh service), ghi thumbnail qua gRPC <c>PutThumbnail</c> ⇒ Media không cần khoá truy
/// cập object storage. Idempotent: giao lại chỉ ghi đè cùng object rồi publish lại cùng event.
/// Nội dung hỏng / không hỗ trợ ⇒ bỏ qua (không retry); lỗi mạng / Storage ⇒ ném lại để retry.
/// </summary>
public sealed partial class MediaThumbnailConsumer(
    StorageService.StorageServiceClient storage,
    IHttpClientFactory httpClientFactory,
    VideoFrameExtractor video,
    MediaOptions options,
    ILogger<MediaThumbnailConsumer> logger) : IConsumer<FileVersionAdded>
{
    public const string HttpClientName = "media-download";
    private static readonly int[] Sizes = [256, 1024];

    public async Task Consume(ConsumeContext<FileVersionAdded> context)
    {
        FileVersionAdded e = context.Message;
        CancellationToken ct = context.CancellationToken;
        MediaKind kind = MediaKinds.Of(e.MimeType);
        long limit = kind switch
        {
            MediaKind.Image => options.MaxImageBytes,
            MediaKind.Pdf => options.MaxPdfBytes,
            MediaKind.Video => long.MaxValue, // ffmpeg chỉ đọc phần đầu
            _ => 0,
        };
        if (kind == MediaKind.Unsupported || e.SizeBytes == 0 || e.SizeBytes > limit || (kind == MediaKind.Video && !video.IsAvailable))
        {
            return;
        }

        GetDownloadUrlResponse source = await storage.GetDownloadUrlAsync(new GetDownloadUrlRequest
        {
            BlobId = e.BlobId.ToString(),
            UserId = e.OwnerId.ToString(),
            FileName = "source",
            Internal = true,
        }, cancellationToken: ct);

        SKBitmap bitmap;
        try
        {
            bitmap = await DecodeAsync(kind, source.Url, ct);
        }
        catch (UnsupportedMediaException ex)
        {
            LogSkipped(e.ItemId, e.VersionId, e.MimeType, ex.Message);
            return;
        }

        var stored = new List<int>(Sizes.Length);
        using (bitmap)
        {
            foreach (int size in Sizes)
            {
                byte[] webp = ThumbnailRenderer.RenderWebp(bitmap, size);
                PutThumbnailResponse response = await storage.PutThumbnailAsync(new PutThumbnailRequest
                {
                    BlobId = e.BlobId.ToString(),
                    Size = size,
                    Content = Google.Protobuf.ByteString.CopyFrom(webp),
                }, cancellationToken: ct);
                if (!response.Stored)
                {
                    return; // blob đã bị xoá trong lúc xử lý
                }

                stored.Add(size);
            }
        }

        await context.Publish(new ThumbnailGenerated(e.ItemId, e.OwnerId, e.VersionId, e.BlobId, stored), ct);
        LogGenerated(e.ItemId, e.VersionId, kind);
    }

    private async Task<SKBitmap> DecodeAsync(MediaKind kind, string url, CancellationToken ct)
    {
        int largest = Sizes[^1];
        switch (kind)
        {
            case MediaKind.Video:
                byte[] frame = await video.ExtractAsync(url, largest, ct);
                return ThumbnailRenderer.DecodeImage(frame, largest, options.MaxImagePixels);
            case MediaKind.Pdf:
                return PdfFirstPage.Render(await DownloadAsync(url, options.MaxPdfBytes, ct), largest);
            default:
                return ThumbnailRenderer.DecodeImage(await DownloadAsync(url, options.MaxImageBytes, ct), largest, options.MaxImagePixels);
        }
    }

    private async Task<byte[]> DownloadAsync(string url, long maxBytes, CancellationToken ct)
    {
        HttpClient http = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new UnsupportedMediaException("File is too large for a thumbnail.");
        }

        await using Stream body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new UnsupportedMediaException("File is too large for a thumbnail.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Thumbnail for item {ItemId} version {VersionId} ({Kind})")]
    private partial void LogGenerated(Guid itemId, Guid versionId, MediaKind kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "No thumbnail for item {ItemId} version {VersionId} ({MimeType}): {Reason}")]
    private partial void LogSkipped(Guid itemId, Guid versionId, string mimeType, string reason);
}

/// <summary>Giới hạn số file xử lý đồng thời (CPU / bộ nhớ) — docs/04: Media worker 2–4.</summary>
public sealed class MediaThumbnailConsumerDefinition : ConsumerDefinition<MediaThumbnailConsumer>
{
    public MediaThumbnailConsumerDefinition(MediaOptions options)
    {
        EndpointName = "media-thumbnails";
        ConcurrentMessageLimit = Math.Max(1, options.ConcurrentMessageLimit);
    }
}
