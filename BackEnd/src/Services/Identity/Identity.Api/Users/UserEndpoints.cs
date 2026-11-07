using Carter;
using FluentValidation;
using Identity.Api.Auth;
using Identity.Api.Data;
using Identity.Api.Tokens;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;
using MyCloud.Contracts.Events.Identity;
using ValidationResult = MyCloud.BuildingBlocks.Domain.Results.ValidationResult;

namespace Identity.Api.Users;

public sealed record UserProfileResponse(Guid Id, string Email, string DisplayName, string? AvatarUrl, DateTimeOffset CreatedAt);

public sealed record UpdateProfileRequest(string DisplayName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Hồ sơ của chính người dùng đang đăng nhập (yêu cầu access token).</summary>
public sealed class UserEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var me = app.NewVersionedApi("Users")
            .MapGroup("/api/v{version:apiVersion}/users/me")
            .HasApiVersion(1)
            .RequireAuthorization();

        me.MapGet("/", GetMeAsync).WithName("GetMe");
        me.MapPatch("/", UpdateMeAsync).WithName("UpdateMe");
        me.MapPost("/password", ChangePasswordAsync).WithName("ChangePassword");
    }

    private static async Task<IResult> GetMeAsync(ICurrentUser currentUser, UserManager<AppUser> users)
    {
        AppUser? user = await users.FindByIdAsync(currentUser.GetRequiredUserId().ToString());
        return user is null ? Results.NotFound() : Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> UpdateMeAsync(
        UpdateProfileRequest request,
        IValidator<UpdateProfileRequest> validator,
        ICurrentUser currentUser,
        UserManager<AppUser> users,
        IPublishEndpoint publishEndpoint,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        AppUser? user = await users.FindByIdAsync(currentUser.GetRequiredUserId().ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        string displayName = request.DisplayName.Trim();
        if (displayName != user.DisplayName)
        {
            user.DisplayName = displayName;
            // Outbox: event lưu cùng transaction với UpdateAsync.
            await publishEndpoint.Publish(new UserProfileChanged(user.Id, user.DisplayName, user.AvatarUrl), ct);
            IdentityResult result = await users.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return ValidationResult.WithErrors(result.Errors.Select(e => new Error($"IDENTITY.{e.Code}", e.Description)).ToArray()).ToProblem();
            }
        }

        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        IValidator<ChangePasswordRequest> validator,
        ICurrentUser currentUser,
        AccountService accounts,
        RefreshTokenCookie cookie,
        HttpContext context,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        Result result = await accounts.ChangePasswordAsync(currentUser.GetRequiredUserId(), request, ct);
        if (result.IsFailure)
        {
            return result.Error == IdentityErrors.UserNotFound ? Results.NotFound() : result.ToProblem();
        }

        // Mọi phiên (kể cả phiên hiện tại) đã bị thu hồi: client đăng nhập lại bằng mật khẩu mới.
        cookie.Delete(context.Response);
        return Results.NoContent();
    }

    private static UserProfileResponse ToResponse(AppUser user) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, user.AvatarUrl, user.CreatedAt);
}

internal sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator() =>
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(AppUser.DisplayNameMaxLength);
}

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128).NotEqual(x => x.CurrentPassword);
    }
}
