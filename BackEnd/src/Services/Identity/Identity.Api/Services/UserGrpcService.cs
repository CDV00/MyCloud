using Grpc.Core;
using Identity.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.Contracts.Identity.V1;

namespace Identity.Api.Services;

/// <summary>Tra cứu người dùng cho service khác (docs/03 §2). Chỉ định danh service được gọi.</summary>
[Authorize(Policy = ServiceIdentity.Policy)]
public sealed class UserGrpcService(UserManager<AppUser> users, AppIdentityDbContext db) : UserService.UserServiceBase
{
    private const int MaxBatch = 200;

    public override async Task<FindUserByEmailResponse> FindUserByEmail(FindUserByEmailRequest request, ServerCallContext context)
    {
        AppUser? user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        return user is null ? new FindUserByEmailResponse { Found = false } : new FindUserByEmailResponse { Found = true, User = ToInfo(user) };
    }

    public override async Task<BatchGetUsersResponse> BatchGetUsers(BatchGetUsersRequest request, ServerCallContext context)
    {
        Guid[] ids = request.Ids.Select(id => Guid.TryParse(id, out Guid g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().Take(MaxBatch).ToArray();
        List<AppUser> found = ids.Length == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(context.CancellationToken);

        var response = new BatchGetUsersResponse();
        response.Users.AddRange(found.Select(ToInfo));
        return response;
    }

    private static UserInfo ToInfo(AppUser user) => new()
    {
        Id = user.Id.ToString(),
        Email = user.Email ?? string.Empty,
        DisplayName = user.DisplayName,
        AvatarUrl = user.AvatarUrl ?? string.Empty,
    };
}
