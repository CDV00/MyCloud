namespace Storage.Api.ObjectStorage;

/// <summary>
/// Kiểu nội dung được phép hiển thị trực tiếp (<c>inline</c>) trong trình duyệt. URL tải nằm cùng origin với ứng dụng
/// (Vite/nginx proxy <c>/mycloud-blobs/</c> — ADR-013) và MIME do client khai báo lúc upload ⇒ một file HTML/SVG được
/// mở inline sẽ chạy script trên origin của MyCloud (stored XSS). Chỉ ảnh raster, video, audio, PDF được giữ nguyên
/// kiểu; văn bản (kể cả HTML, JS, SVG, JSON) bị ép thành <c>text/plain</c>; còn lại chỉ được tải về (docs/06 §3).
/// </summary>
public static class InlineContent
{
    public const string PlainText = "text/plain; charset=utf-8";

    private static readonly HashSet<string> TextLike = new(StringComparer.Ordinal)
    {
        "application/json", "application/xml", "application/javascript", "application/x-javascript", "application/x-sh",
        "application/x-yaml", "application/yaml", "application/sql", "application/x-httpd-php", "application/xhtml+xml",
        "application/x-ndjson", "application/toml", "application/x-python-code", "image/svg+xml",
    };

    /// <returns>Kiểu nội dung trả về và có được hiển thị inline hay không.</returns>
    public static (string ContentType, bool Inline) Resolve(string? mimeType, bool inline)
    {
        string mime = (mimeType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (!inline)
        {
            return (string.IsNullOrEmpty(mime) ? "application/octet-stream" : mime, false);
        }

        if (mime == "image/svg+xml" || mime.StartsWith("text/", StringComparison.Ordinal) || TextLike.Contains(mime))
        {
            return (PlainText, true);
        }

        if ((mime.StartsWith("image/", StringComparison.Ordinal) || mime.StartsWith("video/", StringComparison.Ordinal)
                || mime.StartsWith("audio/", StringComparison.Ordinal) || mime == "application/pdf")
            && !mime.Contains("xml", StringComparison.Ordinal))
        {
            return (mime, true);
        }

        return ("application/octet-stream", false);
    }
}
