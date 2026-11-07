using SkiaSharp;

namespace Media.Worker.Thumbnails;

/// <summary>Nội dung không phải ảnh hợp lệ / không giải mã được — bỏ qua (không retry).</summary>
public sealed class UnsupportedMediaException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Giải mã ảnh và tạo thumbnail WebP (Skia). Ảnh lớn được giải mã ở độ phân giải thấp ngay từ codec (JPEG/WebP hỗ trợ
/// lấy mẫu) nên không phải dựng bitmap đầy đủ; ảnh vượt <c>maxPixels</c> bị từ chối (chống "pixel bomb").
/// </summary>
public static class ThumbnailRenderer
{
    public const int Quality = 80;

    /// <summary>Giải mã đủ lớn để cắt thumbnail <paramref name="targetSize"/> sắc nét, đã xoay theo EXIF.</summary>
    public static SKBitmap DecodeImage(byte[] data, int targetSize, long maxPixels)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(data))
            ?? throw new UnsupportedMediaException("Not a supported image.");
        SKImageInfo info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > maxPixels)
        {
            throw new UnsupportedMediaException($"Image dimensions {info.Width}x{info.Height} are not supported.");
        }

        // Lấy mẫu: kích thước giải mã nhỏ nhất mà cạnh dài vẫn ≥ targetSize.
        float scale = Math.Min(1f, (float)targetSize / Math.Max(info.Width, info.Height));
        SKSizeI scaled = codec.GetScaledDimensions(scale);
        if (Math.Max(scaled.Width, scaled.Height) < Math.Min(targetSize, Math.Max(info.Width, info.Height)))
        {
            scaled = new SKSizeI(info.Width, info.Height);
        }

        var decodeInfo = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(decodeInfo);
        SKCodecResult result = codec.GetPixels(decodeInfo, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new UnsupportedMediaException($"Image could not be decoded ({result}).");
        }

        return Orient(bitmap, codec.EncodedOrigin);
    }

    /// <summary>Thu nhỏ để cạnh dài ≤ <paramref name="size"/> (không phóng to) và mã hoá WebP.</summary>
    public static byte[] RenderWebp(SKBitmap source, int size)
    {
        float scale = Math.Min(1f, (float)size / Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (SKImage image = SKImage.FromBitmap(source))
        {
            canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        using SKImage snapshot = surface.Snapshot();
        using SKData encoded = snapshot.Encode(SKEncodedImageFormat.Webp, Quality)
            ?? throw new InvalidOperationException("WebP encoding failed.");
        return encoded.ToArray();
    }

    /// <summary>Ảnh chụp bằng điện thoại thường lưu xoay trong EXIF — áp dụng để thumbnail đúng chiều.</summary>
    internal static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return bitmap;
        }

        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var rotated = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        using (var canvas = new SKCanvas(rotated))
        {
            int w = bitmap.Width;
            int h = bitmap.Height;
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Scale(-1, 1, w / 2f, 0);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.RotateDegrees(180, w / 2f, h / 2f);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Scale(1, -1, 0, h / 2f);
                    break;
                case SKEncodedOrigin.LeftTop: // chuyển vị: (x, y) → (y, x)
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop: // xoay 90° theo chiều kim đồng hồ: (x, y) → (h − y, x)
                    canvas.Translate(h, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom: // (x, y) → (h − y, w − x)
                    canvas.Translate(h, 0);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    canvas.Translate(-w, 0);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, w);
                    canvas.RotateDegrees(-90);
                    break;
            }

            using SKImage source = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(source, 0, 0, SKSamplingOptions.Default);
        }

        bitmap.Dispose();
        return rotated;
    }
}
