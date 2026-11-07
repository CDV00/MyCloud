using Identity.Api.Data;
using Identity.Api.Tokens;
using Identity.Api.Users;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.Contracts.Events.Identity;
using ValidationResult = MyCloud.BuildingBlocks.Domain.Results.ValidationResult;

namespace Identity.Api.Auth;

public sealed record TokenPair(AccessToken AccessToken, IssuedRefreshToken RefreshToken);

public static class IdentityErrors
{
    public static readonly Error EmailTaken = new("IDENTITY.EMAIL_TAKEN", "An account with this email already exists.");

    // Thông báo chung cho mọi lỗi đăng nhập: không tiết lộ email có tồn tại / bị khoá hay không (lỗi S9 của bản cũ).
    public static readonly Error InvalidCredentials = new("IDENTITY.INVALID_CREDENTIALS", "Invalid email or password.");

    public static readonly Error InvalidRefreshToken = new("IDENTITY.INVALID_REFRESH_TOKEN", "The session has expired. Please sign in again.");

    public static readonly Error PasswordMismatch = new("IDENTITY.PASSWORD_MISMATCH", "The current password is incorrect.");

    public static readonly Error UserNotFound = new("IDENTITY.USER_NOT_FOUND", "The user does not exist.");
}

/// <summary>Đăng ký, đăng nhập, refresh, đăng xuất, đổi mật khẩu.</summary>
public sealed class AccountService(
    UserManager<AppUser> userManager,
    AppIdentityDbContext db,
    AccessTokenService accessTokens,
    RefreshTokenService refreshTokens,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider)
{
    public async Task<Result<Guid>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Result.Failure<Guid>(IdentityErrors.EmailTaken);
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = timeProvider.GetUtcNow(),
        };

        // Bus outbox: event được thêm vào DbContext và lưu CÙNG transaction với user trong CreateAsync.
        await publishEndpoint.Publish(new UserRegistered(user.Id, user.Email, user.DisplayName), cancellationToken);

        IdentityResult result = await userManager.CreateAsync(user, request.Password);
        return result.Succeeded ? user.Id : ToValidationResult<Guid>(result);
    }

    public async Task<Result<TokenPair>> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        AppUser? user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return Result.Failure<TokenPair>(IdentityErrors.InvalidCredentials);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user); // đủ 5 lần ⇒ khoá 15 phút
            return Result.Failure<TokenPair>(IdentityErrors.InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        IssuedRefreshToken refresh = refreshTokens.Issue(user.Id, familyId: null, client);
        await db.SaveChangesAsync(cancellationToken);

        return new TokenPair(accessTokens.Create(user), refresh);
    }

    public async Task<Result<TokenPair>> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken cancellationToken)
    {
        RefreshToken? current = await refreshTokens.FindForRotationAsync(refreshToken, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TokenPair>(IdentityErrors.InvalidRefreshToken);
        }

        AppUser? user = await userManager.FindByIdAsync(current.UserId.ToString());
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, RevocationReasons.UserUnavailable, cancellationToken);
            return Result.Failure<TokenPair>(IdentityErrors.InvalidRefreshToken);
        }

        IssuedRefreshToken next = refreshTokens.Rotate(current, client);
        await db.SaveChangesAsync(cancellationToken);

        return new TokenPair(accessTokens.Create(user), next);
    }

    public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken) =>
        refreshToken is null
            ? Task.CompletedTask
            : refreshTokens.RevokeFamilyOfAsync(refreshToken, RevocationReasons.Logout, cancellationToken);

    /// <summary>Đổi mật khẩu và đăng xuất mọi phiên (thu hồi mọi refresh token).</summary>
    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        AppUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        IdentityResult result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                ? Result.Failure(IdentityErrors.PasswordMismatch)
                : ToValidationResult(result);
        }

        await refreshTokens.RevokeAllForUserAsync(user.Id, RevocationReasons.PasswordChanged, cancellationToken);
        return Result.Success();
    }

    private static ValidationResult<T> ToValidationResult<T>(IdentityResult result) =>
        ValidationResult<T>.WithErrors(ToErrors(result));

    private static ValidationResult ToValidationResult(IdentityResult result) =>
        ValidationResult.WithErrors(ToErrors(result));

    private static Error[] ToErrors(IdentityResult result) =>
        result.Errors.Select(e => new Error($"IDENTITY.{e.Code}", e.Description)).ToArray();
}
