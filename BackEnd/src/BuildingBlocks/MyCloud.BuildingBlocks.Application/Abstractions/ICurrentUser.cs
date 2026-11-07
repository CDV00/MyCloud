namespace MyCloud.BuildingBlocks.Application.Abstractions;

/// <summary>Người dùng của request hiện tại (đọc từ claim "sub" của JWT).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Id người dùng, null nếu request ẩn danh.</summary>
    Guid? UserId { get; }
}

public static class CurrentUserExtensions
{
    /// <summary>
    /// Id người dùng đã đăng nhập. Endpoint mặc định yêu cầu đăng nhập nên thiếu id là lỗi cấu hình
    /// (endpoint AllowAnonymous gọi vào handler cần user) → 401, không bao giờ "chạy tiếp với Guid.Empty".
    /// </summary>
    public static Guid GetRequiredUserId(this ICurrentUser currentUser) =>
        currentUser.UserId ?? throw new UnauthorizedAccessException("The request is not authenticated.");
}
