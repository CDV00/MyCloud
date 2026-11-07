using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MyCloud.BuildingBlocks.Application.Abstractions;

namespace MyCloud.BuildingBlocks.Web;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            ClaimsPrincipal? user = accessor.HttpContext?.User;
            string? sub = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out Guid id) ? id : null;
        }
    }
}
