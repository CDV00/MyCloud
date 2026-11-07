namespace MyCloud.BuildingBlocks.Application.Abstractions;

/// <summary>Người dùng của request hiện tại (đọc từ claim "sub" của JWT).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Id người dùng, null nếu request ẩn danh.</summary>
    Guid? UserId { get; }
}
