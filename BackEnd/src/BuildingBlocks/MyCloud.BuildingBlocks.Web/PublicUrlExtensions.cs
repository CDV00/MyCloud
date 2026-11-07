using Microsoft.AspNetCore.Http;

namespace MyCloud.BuildingBlocks.Web;

public static class PublicUrlExtensions
{
    /// <summary>
    /// Gốc URL mà trình duyệt đang dùng (vd <c>http://mycloud.localhost</c>, <c>http://10.0.0.5:8081</c>), lấy từ
    /// <c>X-Forwarded-Proto/Host</c> do Gateway đặt, nếu không có thì từ chính request. Dùng để ký presigned URL
    /// cùng origin với web app (không cần CORS). Trả null nếu giá trị không hợp lệ.
    /// </summary>
    public static string? GetPublicBaseUrl(this HttpRequest request)
    {
        string scheme = FirstValue(request.Headers["X-Forwarded-Proto"]) ?? request.Scheme;
        string host = FirstValue(request.Headers["X-Forwarded-Host"]) ?? request.Host.Value ?? string.Empty;

        if (scheme is not ("http" or "https") || host.Length is 0 or > 255
            || !Uri.TryCreate($"{scheme}://{host}", UriKind.Absolute, out Uri? uri)
            || uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string? FirstValue(string? header) =>
        string.IsNullOrWhiteSpace(header) ? null : header.Split(',')[0].Trim();
}
