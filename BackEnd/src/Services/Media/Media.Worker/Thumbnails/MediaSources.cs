using System.ComponentModel;
using System.Diagnostics;
using PDFtoImage;
using SkiaSharp;

namespace Media.Worker.Thumbnails;

public enum MediaKind
{
    Unsupported,
    Image,
    Pdf,
    Video,
}

/// <summary>Section <c>Media</c>.</summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>Ảnh lớn hơn ⇒ bỏ qua (đọc cả file vào bộ nhớ để giải mã).</summary>
    public long MaxImageBytes { get; set; } = 50L * 1024 * 1024;

    public long MaxPdfBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>Số pixel tối đa của ảnh gốc (chống ảnh nén nhỏ nhưng giải mã ra hàng chục GB).</summary>
    public long MaxImagePixels { get; set; } = 120_000_000;

    /// <summary>Đường dẫn ffmpeg; bỏ trống ⇒ tìm "ffmpeg" trong PATH. Không có ffmpeg ⇒ video không có thumbnail.</summary>
    public string? FfmpegPath { get; set; }

    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Số file xử lý đồng thời trên mỗi instance (CPU / bộ nhớ).</summary>
    public int ConcurrentMessageLimit { get; set; } = 2;
}

public static class MediaKinds
{
    // Định dạng Skia giải mã được trên mọi nền tảng. SVG cố ý không hỗ trợ (vẽ vector + có thể tham chiếu tài nguyên ngoài).
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/pjpeg", "image/png", "image/webp", "image/gif", "image/bmp", "image/x-ms-bmp",
        "image/vnd.microsoft.icon", "image/x-icon",
    };

    public static MediaKind Of(string? mimeType)
    {
        string mime = (mimeType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return mime switch
        {
            _ when Images.Contains(mime) => MediaKind.Image,
            "application/pdf" => MediaKind.Pdf,
            _ when mime.StartsWith("video/", StringComparison.Ordinal) => MediaKind.Video,
            _ => MediaKind.Unsupported,
        };
    }
}

/// <summary>Trang đầu PDF (PDFium) thành bitmap.</summary>
public static class PdfFirstPage
{
    public static SKBitmap Render(byte[] pdf, int targetSize)
    {
        try
        {
            // Worker chỉ chạy trên Windows (dev) / Linux (container) — đều có PDFium native.
#pragma warning disable CA1416
            // Chỉ đặt cạnh dài: đặt cả hai cạnh thì PDFium kéo giãn trang thành hình vuông.
            System.Drawing.SizeF page = Conversion.GetPageSize(pdf, page: (Index)0);
            RenderOptions options = page.Width >= page.Height
                ? new RenderOptions(Width: targetSize, WithAspectRatio: true, BackgroundColor: SKColors.White)
                : new RenderOptions(Height: targetSize, WithAspectRatio: true, BackgroundColor: SKColors.White);
            return Conversion.ToImage(pdf, page: (Index)0, options: options);
#pragma warning restore CA1416
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new UnsupportedMediaException("PDF could not be rendered.", ex);
        }
    }
}

/// <summary>
/// Một khung hình video bằng <c>ffmpeg</c> (tiến trình riêng). Đọc thẳng từ presigned URL nội bộ ⇒ ffmpeg chỉ tải phần
/// cần thiết (HTTP range) thay vì cả file. Không có ffmpeg ⇒ <see cref="IsAvailable"/> = false, video bị bỏ qua.
/// </summary>
public sealed class VideoFrameExtractor(MediaOptions options)
{
    private readonly Lazy<bool> _available = new(() => Probe(options.FfmpegPath ?? "ffmpeg"));

    public bool IsAvailable => _available.Value;

    /// <returns>Ảnh PNG của khung hình ở giây thứ 1 (hoặc khung đầu với video ngắn hơn).</returns>
    public async Task<byte[]> ExtractAsync(string url, int maxSize, CancellationToken ct)
    {
        byte[] frame = await RunAsync(url, seekSeconds: 1, maxSize, ct);
        if (frame.Length == 0)
        {
            frame = await RunAsync(url, seekSeconds: 0, maxSize, ct); // video < 1 giây
        }

        return frame.Length > 0 ? frame : throw new UnsupportedMediaException("No video frame could be extracted.");
    }

    private async Task<byte[]> RunAsync(string url, int seekSeconds, int maxSize, CancellationToken ct)
    {
        var info = new ProcessStartInfo(options.FfmpegPath ?? "ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in Arguments(url, seekSeconds, maxSize))
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.ProcessTimeout);
        try
        {
            using var output = new MemoryStream();
            Task copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(copy, errors, process.WaitForExitAsync(timeout.Token));
            return process.ExitCode == 0 ? output.ToArray() : [];
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new UnsupportedMediaException("ffmpeg timed out.");
        }
    }

    /// <summary>
    /// <c>-ss</c> trước <c>-i</c>: tua nhanh theo keyframe; chỉ cho phép giao thức http(s) (URL từ Storage) để file video
    /// độc hại không khiến ffmpeg đọc file cục bộ / giao thức khác qua playlist.
    /// </summary>
    internal static IReadOnlyList<string> Arguments(string url, int seekSeconds, int maxSize) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin",
        "-protocol_whitelist", "http,https,tcp,tls",
        "-ss", seekSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-i", url,
        "-frames:v", "1",
        "-vf", $"scale='min({maxSize},iw)':'min({maxSize},ih)':force_original_aspect_ratio=decrease",
        "-f", "image2pipe", "-vcodec", "png", "pipe:1",
    ];

    private static bool Probe(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            return process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
