using Identity.Api.Data;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.Contracts.Events.Identity;

namespace Identity.Api.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public static class IdentityErrors
{
    public static readonly Error EmailTaken = new("IDENTITY.EMAIL_TAKEN", "An account with this email already exists.");

    // Thông báo chung cho mọi lỗi đăng nhập: không tiết lộ email có tồn tại hay không (lỗi S9 của bản cũ).
    public static readonly Error InvalidCredentials = new("IDENTITY.INVALID_CREDENTIALS", "Invalid email or password.");
}

public sealed class AccountService(
    UserManager<AppUser> userManager,
    JwtTokenService tokenService,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider)
{
    public async Task<Result<Guid>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Result.Failure<Guid>(IdentityErrors.EmailTaken);
        }

        // Sửa lỗi S8: bản cũ map vào biến null rồi gọi CreateAsync(null).
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
        if (!result.Succeeded)
        {
            Error[] errors = result.Errors.Select(e => new Error($"IDENTITY.{e.Code}", e.Description)).ToArray();
            return ValidationResult<Guid>.WithErrors(errors);
        }

        return user.Id;
    }

    public async Task<Result<TokenResponse>> LoginAsync(LoginRequest request)
    {
        AppUser? user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return Result.Failure<TokenResponse>(IdentityErrors.InvalidCredentials);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return Result.Failure<TokenResponse>(IdentityErrors.InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        AccessToken token = tokenService.CreateAccessToken(user);
        int expiresIn = (int)(token.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds;

        // Refresh token (lưu hash + rotation) được làm ở Phase 1.
        return new TokenResponse(token.Token, "Bearer", expiresIn);
    }
}
