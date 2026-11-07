using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace MyCloud.BuildingBlocks.Authentication;

/// <summary>
/// Quản trị viên hệ thống: access token mang claim <c>role = admin</c> (Identity phát hành). Mọi service tự kiểm tra bằng
/// policy <see cref="Policy"/> — không gọi Identity. Thu hồi quyền có hiệu lực khi access token hiện tại hết hạn (≤ 15 phút).
/// </summary>
public static class AdminRole
{
    public const string ClaimType = "role";
    public const string Value = "admin";
    public const string Policy = "admin";

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.Claims.Any(c => (c.Type == ClaimType || c.Type == ClaimTypes.Role) && c.Value == Value);

    internal static AuthorizationPolicy BuildPolicy() =>
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(c => c.User.IsAdmin()).Build();
}
