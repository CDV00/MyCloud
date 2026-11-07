using System.Globalization;
using System.Text;
using Media.Worker.Thumbnails;
using SkiaSharp;

namespace Media.Worker.Tests;

public sealed class ThumbnailRendererTests
{
    [Theory]
    [InlineData("image/jpeg", MediaKind.Image)]
    [InlineData("IMAGE/PNG; charset=binary", MediaKind.Image)]
    [InlineData("image/svg+xml", MediaKind.Unsupported)]
    [InlineData("application/pdf", MediaKind.Pdf)]
    [InlineData("video/mp4", MediaKind.Video)]
    [InlineData("text/plain", MediaKind.Unsupported)]
    [InlineData(null, MediaKind.Unsupported)]
    public void MediaKinds_ClassifiesByMimeType(string? mime, MediaKind expected) => MediaKinds.Of(mime).ShouldBe(expected);

    [Fact]
    public void LargeImage_IsScaledToFit_KeepingAspectRatio_AsWebp()
    {
        byte[] png = Png(2000, 1000, SKColors.Orange);

        using SKBitmap decoded = ThumbnailRenderer.DecodeImage(png, 1024, maxPixels: 10_000_000);
        byte[] small = ThumbnailRenderer.RenderWebp(decoded, 256);
        byte[] large = ThumbnailRenderer.RenderWebp(decoded, 1024);

        Dimensions(small).ShouldBe((256, 128));
        Dimensions(large).ShouldBe((1024, 512));
        Encoding.ASCII.GetString(small, 8, 4).ShouldBe("WEBP");
    }

    [Fact]
    public void SmallImage_IsNeverUpscaled()
    {
        using SKBitmap decoded = ThumbnailRenderer.DecodeImage(Png(40, 30, SKColors.Red), 1024, maxPixels: 10_000_000);
        Dimensions(ThumbnailRenderer.RenderWebp(decoded, 256)).ShouldBe((40, 30));
    }

    [Fact]
    public void ImagesAbovePixelLimit_AndGarbage_AreUnsupported()
    {
        Should.Throw<UnsupportedMediaException>(() => ThumbnailRenderer.DecodeImage(Png(200, 200, SKColors.Blue), 256, maxPixels: 10_000));
        Should.Throw<UnsupportedMediaException>(() => ThumbnailRenderer.DecodeImage(Encoding.UTF8.GetBytes("không phải ảnh"), 256, 10_000_000));
    }

    // Ảnh 2×1: trái đỏ, phải xanh. Kết quả 1×2 (trên, dưới) theo từng hướng EXIF xoay 90°.
    [Theory]
    [InlineData(SKEncodedOrigin.RightTop, "red", "blue")]    // EXIF 6: xoay 90° theo chiều kim đồng hồ
    [InlineData(SKEncodedOrigin.LeftBottom, "blue", "red")]  // EXIF 8: xoay 90° ngược chiều
    [InlineData(SKEncodedOrigin.LeftTop, "red", "blue")]     // EXIF 5: chuyển vị
    [InlineData(SKEncodedOrigin.RightBottom, "blue", "red")] // EXIF 7: chuyển vị ngược
    public void ExifOrientation_IsApplied(SKEncodedOrigin origin, string top, string bottom)
    {
        var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Blue);

        using SKBitmap oriented = ThumbnailRenderer.Orient(bitmap, origin);

        (oriented.Width, oriented.Height).ShouldBe((1, 2));
        Name(oriented.GetPixel(0, 0)).ShouldBe(top);
        Name(oriented.GetPixel(0, 1)).ShouldBe(bottom);
    }

    [Fact]
    public void Pdf_FirstPageIsRendered_FittingTheTargetSize()
    {
        using SKBitmap page = PdfFirstPage.Render(MinimalPdf(widthPt: 600, heightPt: 300), 1024);

        Math.Max(page.Width, page.Height).ShouldBe(1024);
        ((double)page.Width / page.Height).ShouldBe(2.0, tolerance: 0.02);
    }

    [Fact]
    public void BrokenPdf_IsUnsupported() =>
        Should.Throw<UnsupportedMediaException>(() => PdfFirstPage.Render(Encoding.ASCII.GetBytes("%PDF-1.4 hỏng"), 256));

    [Fact]
    public void FfmpegArguments_OnlyAllowNetworkProtocols_AndSeekBeforeInput()
    {
        IReadOnlyList<string> args = VideoFrameExtractor.Arguments("http://minio/x", 1, 1024);

        args[args.ToList().IndexOf("-protocol_whitelist") + 1].ShouldBe("http,https,tcp,tls");
        args.ToList().IndexOf("-ss").ShouldBeLessThan(args.ToList().IndexOf("-i"));
        args[^1].ShouldBe("pipe:1");
    }

    internal static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>PDF một trang hợp lệ tối thiểu (bảng xref tính đúng offset).</summary>
    internal static byte[] MinimalPdf(int widthPt, int heightPt)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt} {heightPt}] >>"),
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static (int Width, int Height) Dimensions(byte[] encoded)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(encoded));
        return (codec.Info.Width, codec.Info.Height);
    }

    private static string Name(SKColor c) => c.Red > 200 && c.Blue < 50 ? "red" : c.Blue > 200 && c.Red < 50 ? "blue" : c.ToString();
}
