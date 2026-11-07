using System.Security.Cryptography;
using Identity.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Keys;

/// <summary>Bộ khoá đang dùng: khoá ký hiện tại + mọi khoá còn được công bố (để validate và trả JWKS).</summary>
public sealed record KeySet(SigningCredentials SigningCredentials, IReadOnlyList<SecurityKey> ValidationKeys, JsonWebKeySetResponse Jwks);

public sealed record JsonWebKeyResponse(string Kty, string Use, string Alg, string Kid, string N, string E);

public sealed record JsonWebKeySetResponse(IReadOnlyList<JsonWebKeyResponse> Keys);

/// <summary>
/// Nạp khoá ký từ DB vào bộ nhớ, tạo và xoay vòng khoá khi cần. Singleton: <see cref="Current"/> đọc được đồng bộ
/// (resolver của JwtBearer là đồng bộ); <see cref="ReloadAsync"/> chạy lúc khởi động và định kỳ.
/// </summary>
public sealed partial class SigningKeyStore(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtection,
    IOptions<SigningKeyOptions> options,
    TimeProvider timeProvider,
    ILogger<SigningKeyStore> logger) : IDisposable
{
    // Khoá advisory của PostgreSQL: chỉ một instance Identity được tạo khoá mới tại một thời điểm.
    private const long RotationLockId = 7_316_001;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("MyCloud.Identity.SigningKeys.v1");
    private readonly SigningKeyOptions _options = options.Value;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly Dictionary<string, RsaSecurityKey> _materialized = new(StringComparer.Ordinal);
    private volatile KeySet? _current;

    public KeySet Current => _current
        ?? throw new InvalidOperationException("Signing keys are not loaded. SigningKeyStore.ReloadAsync must run at startup.");

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
            DateTimeOffset now = timeProvider.GetUtcNow();

            List<UsableKey> keys = await LoadUsableAsync(db, now, cancellationToken);
            if (NeedsNewKey(keys, now))
            {
                await CreateKeyAsync(db, now, cancellationToken);
                keys = await LoadUsableAsync(db, now, cancellationToken);
            }

            _current = BuildKeySet(keys, now);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public void Dispose()
    {
        _reloadLock.Dispose();
        foreach (RsaSecurityKey key in _materialized.Values)
        {
            key.Rsa?.Dispose();
        }
    }

    private bool NeedsNewKey(List<UsableKey> keys, DateTimeOffset now) =>
        keys.Count == 0 || now - keys.Max(k => k.Row.CreatedAt) >= _options.RotationInterval - _options.ActivationDelay;

    private async Task<List<UsableKey>> LoadUsableAsync(AppIdentityDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        DateTimeOffset publishedSince = now - (_options.RotationInterval + _options.ActivationDelay + _options.RetentionAfterRotation);
        List<SigningKey> rows = await db.SigningKeys.AsNoTracking()
            .Where(k => k.RevokedAt == null && k.CreatedAt > publishedSince)
            .OrderBy(k => k.CreatedAt)
            .ToListAsync(ct);

        var usable = new List<UsableKey>(rows.Count);
        foreach (SigningKey row in rows)
        {
            if (TryMaterialize(row) is { } rsa)
            {
                usable.Add(new UsableKey(row, rsa));
            }
        }

        return usable;
    }

    private RsaSecurityKey? TryMaterialize(SigningKey row)
    {
        if (_materialized.TryGetValue(row.Id, out RsaSecurityKey? cached))
        {
            return cached;
        }

        try
        {
            byte[] pkcs8 = _protector.Unprotect(Convert.FromBase64String(row.ProtectedPrivateKey));
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(pkcs8, out _);
            CryptographicOperations.ZeroMemory(pkcs8);

            var key = new RsaSecurityKey(rsa) { KeyId = row.Id };
            _materialized[row.Id] = key;
            return key;
        }
        catch (CryptographicException ex)
        {
            // Thường do mất key ring của Data Protection: bỏ qua khoá này, hệ thống sẽ tạo khoá mới.
            LogUnreadableKey(ex, row.Id);
            return null;
        }
    }

    private async Task CreateKeyAsync(AppIdentityDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        // Aspire bật retry cho Npgsql ⇒ transaction tự mở phải chạy trong execution strategy.
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // lần retry không được lưu lại khoá của lần thử trước
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({RotationLockId})", ct);

            // Instance khác có thể vừa tạo xong khoá trong lúc ta chờ lock.
            if (!NeedsNewKey(await LoadUsableAsync(db, now, ct), now))
            {
                return;
            }

            using RSA rsa = RSA.Create(_options.KeySizeBits);
            byte[] pkcs8 = rsa.ExportPkcs8PrivateKey();
            var key = new SigningKey
            {
                Id = Guid.CreateVersion7().ToString("N"),
                Algorithm = SigningKey.Rs256,
                ProtectedPrivateKey = Convert.ToBase64String(_protector.Protect(pkcs8)),
                CreatedAt = now,
            };
            CryptographicOperations.ZeroMemory(pkcs8);

            db.SigningKeys.Add(key);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            LogKeyCreated(key.Id);
        });
    }

    private KeySet BuildKeySet(List<UsableKey> keys, DateTimeOffset now)
    {
        // Khoá ký: khoá mới nhất đã được công bố đủ lâu. Nếu chưa có (lần chạy đầu tiên, hoặc khoá cũ vừa bị thu hồi)
        // thì dùng khoá mới nhất.
        UsableKey signing = keys.LastOrDefault(k => now - k.Row.CreatedAt >= _options.ActivationDelay) ?? keys[^1];

        JsonWebKeyResponse[] jwks = keys.Select(k =>
        {
            RSAParameters p = k.Rsa.Rsa!.ExportParameters(includePrivateParameters: false);
            return new JsonWebKeyResponse("RSA", "sig", k.Row.Algorithm, k.Row.Id, Base64UrlEncoder.Encode(p.Modulus), Base64UrlEncoder.Encode(p.Exponent));
        }).ToArray();

        return new KeySet(
            new SigningCredentials(signing.Rsa, SecurityAlgorithms.RsaSha256),
            keys.Select(k => (SecurityKey)k.Rsa).ToArray(),
            new JsonWebKeySetResponse(jwks));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created new JWT signing key {KeyId}")]
    private partial void LogKeyCreated(string keyId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Signing key {KeyId} cannot be decrypted (Data Protection key ring lost?) and is ignored")]
    private partial void LogUnreadableKey(Exception exception, string keyId);

    private sealed record UsableKey(SigningKey Row, RsaSecurityKey Rsa);
}

/// <summary>Đọc lại khoá định kỳ: nhận khoá do instance khác tạo và xoay vòng đúng hạn.</summary>
public sealed partial class SigningKeyReloader(
    SigningKeyStore store,
    IOptions<SigningKeyOptions> options,
    TimeProvider timeProvider,
    ILogger<SigningKeyReloader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ReloadInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await store.ReloadAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReloadFailed(ex); // giữ bộ khoá cũ, thử lại ở lần sau
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reloading signing keys failed")]
    private partial void LogReloadFailed(Exception exception);
}
