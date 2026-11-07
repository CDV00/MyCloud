using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Storage.Api.Data;

/// <summary>
/// Thao tác quota bằng câu UPDATE có điều kiện trên một dòng — an toàn khi nhiều upload chạy song song, không cần lock.
/// Các hàm dùng DbContext được truyền vào để chạy chung transaction với thay đổi trạng thái blob.
/// </summary>
public sealed class QuotaStore(IOptions<StorageOptions> options)
{
    /// <summary>Tạo dòng quota mặc định nếu chưa có (user cũ chưa nhận UserRegistered, hoặc event chưa tới).</summary>
    public Task EnsureAsync(StorageDbContext db, Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO quotas (user_id, limit_bytes, used_bytes, reserved_bytes)
            VALUES ({userId}, {options.Value.DefaultQuotaBytes}, 0, 0)
            ON CONFLICT (user_id) DO NOTHING
            """,
            ct);

    /// <summary>Giữ chỗ <paramref name="bytes"/>; false nếu vượt hạn mức.</summary>
    public async Task<bool> TryReserveAsync(StorageDbContext db, Guid userId, long bytes, CancellationToken ct) =>
        await db.Quotas
            .Where(q => q.UserId == userId && q.UsedBytes + q.ReservedBytes + bytes <= q.LimitBytes)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.ReservedBytes, q => q.ReservedBytes + bytes), ct) == 1;

    /// <summary>Upload hoàn tất: bỏ phần giữ chỗ, cộng dung lượng thật.</summary>
    public Task CommitAsync(StorageDbContext db, Guid userId, long reservedBytes, long actualBytes, CancellationToken ct) =>
        db.Quotas
            .Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(q => q.ReservedBytes, q => q.ReservedBytes - reservedBytes)
                    .SetProperty(q => q.UsedBytes, q => q.UsedBytes + actualBytes),
                ct);

    public Task ReleaseReservedAsync(StorageDbContext db, Guid userId, long bytes, CancellationToken ct) =>
        db.Quotas
            .Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.ReservedBytes, q => q.ReservedBytes - bytes), ct);

    public Task ReleaseUsedAsync(StorageDbContext db, Guid userId, long bytes, CancellationToken ct) =>
        db.Quotas
            .Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.UsedBytes, q => q.UsedBytes - bytes), ct);
}
