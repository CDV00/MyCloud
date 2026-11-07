using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyCloud.BuildingBlocks.Authentication;

/// <summary>
/// Vé vào "phòng" realtime của một item (lịch sử, trao đổi, ai đang xem — ADR-017). Drive cấp sau khi đã kiểm tra quyền
/// xem item; Notification (hub SignalR) chỉ kiểm tra chữ ký HMAC — không phải gọi ngược Drive cho mỗi kết nối.
/// Dạng: <c>{itemId:N}.{userId:N}.{hết hạn unix}.{chữ ký base64url}</c>, gắn với đúng người dùng của kết nối.
/// </summary>
public static class LiveRoomToken
{
    public static string Create(string secret, Guid itemId, Guid userId, DateTimeOffset expiresAt)
    {
        string payload = string.Create(
            CultureInfo.InvariantCulture, $"{itemId:N}.{userId:N}.{expiresAt.ToUnixTimeSeconds()}");
        return payload + "." + Sign(secret, payload);
    }

    /// <returns>Id item nếu vé hợp lệ, của đúng <paramref name="userId"/> và chưa hết hạn; ngược lại null.</returns>
    public static Guid? Verify(string secret, string? token, Guid userId, DateTimeOffset now)
    {
        string[] parts = (token ?? string.Empty).Split('.');
        if (parts.Length != 4
            || !Guid.TryParseExact(parts[0], "N", out Guid itemId)
            || !Guid.TryParseExact(parts[1], "N", out Guid tokenUser) || tokenUser != userId
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long expires)
            || DateTimeOffset.FromUnixTimeSeconds(expires) < now)
        {
            return null;
        }

        byte[] expected = Encoding.ASCII.GetBytes(Sign(secret, $"{parts[0]}.{parts[1]}.{parts[2]}"));
        byte[] actual = Encoding.ASCII.GetBytes(parts[3]);
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? itemId : null;
    }

    private static string Sign(string secret, string payload)
    {
        byte[] mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
