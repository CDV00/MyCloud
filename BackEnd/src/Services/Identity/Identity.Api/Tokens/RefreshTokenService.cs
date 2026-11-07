using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Identity.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Api.Tokens;

public sealed record ClientInfo(string? IpAddress, string? UserAgent);

public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Phát hành, xoay vòng và thu hồi refresh token. Các hàm Issue/Rotate chỉ thêm/sửa entity trong DbContext;
/// người gọi lưu cùng các thay đổi khác (một transaction). Các hàm Revoke* ghi thẳng xuống DB.
/// </summary>
public sealed partial class RefreshTokenService(
    AppIdentityDbContext db,
    IOptions<TokenOptions> options,
    TimeProvider timeProvider,
    ILogger<RefreshTokenService> logger)
{
    private const int TokenBytes = 32; // 256 bit

    private readonly TokenOptions _options = options.Value;

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    /// <summary>Token mới cho một family (null = family mới, tức một phiên đăng nhập mới).</summary>
    public IssuedRefreshToken Issue(Guid userId, Guid? familyId, ClientInfo client) => IssueEntity(userId, familyId, client).Issued;

    /// <summary>
    /// Tìm token để xoay vòng. Trả null nếu token không hợp lệ. Token đã bị xoay vòng mà bị dùng lại (ngoài khoảng
    /// <see cref="TokenOptions.RefreshReuseLeeway"/>) ⇒ thu hồi cả family.
    /// </summary>
    public async Task<RefreshToken?> FindForRotationAsync(string presented, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presented) || presented.Length > 128)
        {
            return null;
        }

        string hash = Hash(presented);
        RefreshToken? token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null)
        {
            return null;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (token.RevokedAt is { } revokedAt)
        {
            bool withinLeeway = token.RevokedReason == RevocationReasons.Rotated && now - revokedAt <= _options.RefreshReuseLeeway;
            if (withinLeeway)
            {
                // Chỉ khi phiên vẫn còn sống: sau logout/đổi mật khẩu thì không còn token nào active trong family.
                bool familyActive = await db.RefreshTokens.AnyAsync(
                    t => t.FamilyId == token.FamilyId && t.RevokedAt == null, cancellationToken);
                return familyActive ? token : null;
            }

            if (token.RevokedReason == RevocationReasons.Rotated)
            {
                LogReuseDetected(token.UserId, token.FamilyId);
                await RevokeFamilyAsync(token.FamilyId, RevocationReasons.ReuseDetected, cancellationToken);
            }

            return null;
        }

        return token.ExpiresAt <= now ? null : token;
    }

    /// <summary>Thay <paramref name="current"/> bằng token mới cùng family.</summary>
    public IssuedRefreshToken Rotate(RefreshToken current, ClientInfo client)
    {
        (IssuedRefreshToken issued, RefreshToken next) = IssueEntity(current.UserId, current.FamilyId, client);

        // Trong khoảng leeway, token cũ đã bị xoay vòng rồi: giữ nguyên liên kết cũ, chỉ cấp thêm token anh em.
        if (current.RevokedAt is null)
        {
            current.RevokedAt = timeProvider.GetUtcNow();
            current.RevokedReason = RevocationReasons.Rotated;
            current.ReplacedById = next.Id;
        }

        return issued;
    }

    public Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), cancellationToken);
    }

    /// <summary>Đăng xuất: thu hồi family của token được gửi lên (bỏ qua nếu token không tồn tại).</summary>
    public async Task RevokeFamilyOfAsync(string presented, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presented) || presented.Length > 128)
        {
            return;
        }

        string hash = Hash(presented);
        Guid? familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(cancellationToken);
        if (familyId is { } id)
        {
            await RevokeFamilyAsync(id, reason, cancellationToken);
        }
    }

    public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), cancellationToken);
    }

    private (IssuedRefreshToken Issued, RefreshToken Entity) IssueEntity(Guid userId, Guid? familyId, ClientInfo client)
    {
        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        DateTimeOffset now = timeProvider.GetUtcNow();

        var entity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FamilyId = familyId ?? Guid.CreateVersion7(),
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now + _options.RefreshTokenLifetime,
            CreatedByIp = Truncate(client.IpAddress, 64),
            UserAgent = Truncate(client.UserAgent, 512),
        };
        db.RefreshTokens.Add(entity);

        return (new IssuedRefreshToken(token, entity.ExpiresAt), entity);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Refresh token reuse detected for user {UserId}, family {FamilyId}: the whole family has been revoked")]
    private partial void LogReuseDetected(Guid userId, Guid familyId);
}

/// <summary>Dọn refresh token đã hết hạn, chạy mỗi giờ.</summary>
public sealed partial class ExpiredRefreshTokenCleanup(
    IServiceScopeFactory scopeFactory,
    IOptions<TokenOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiredRefreshTokenCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
                DateTimeOffset cutoff = timeProvider.GetUtcNow() - options.Value.ExpiredTokenRetention;
                int deleted = await db.RefreshTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                {
                    LogDeleted(deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired refresh tokens")]
    private partial void LogDeleted(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up expired refresh tokens failed")]
    private partial void LogFailed(Exception exception);
}
