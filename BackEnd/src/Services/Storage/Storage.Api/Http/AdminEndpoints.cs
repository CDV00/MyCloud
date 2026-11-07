using Carter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Authentication;
using Storage.Api.Data;

namespace Storage.Api.Http;

public sealed record AdminQuota(Guid UserId, long LimitBytes, long UsedBytes, long ReservedBytes);

public sealed record AdminStorageSummary(long UsedBytes, long LimitBytes, long Blobs, long DefaultQuotaBytes);

public sealed record SetQuotaRequest(long LimitBytes);

/// <summary>
/// Quản trị dung lượng (chỉ quản trị viên): xem dung lượng của nhiều người dùng một lần, đổi hạn mức, tổng quan hệ thống.
/// Hạ hạn mức xuống dưới phần đã dùng là hợp lệ: file cũ giữ nguyên, chỉ không tải lên thêm được.
/// </summary>
public sealed class AdminEndpoints : ICarterModule
{
    public const long MaxQuotaBytes = 1024L * 1024 * 1024 * 1024 * 1024; // 1 PB — chặn giá trị vô lý

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var admin = app.NewVersionedApi("StorageAdmin")
            .MapGroup("/api/v{version:apiVersion}/admin")
            .HasApiVersion(1)
            .RequireAuthorization(AdminRole.Policy);

        admin.MapGet("/summary", SummaryAsync).WithName("AdminStorageSummary");
        admin.MapGet("/quotas", ListAsync).WithName("AdminListQuotas");
        admin.MapPut("/quotas/{userId:guid}", SetAsync).WithName("AdminSetQuota");
    }

    private static async Task<IResult> SummaryAsync(StorageDbContext db, IOptions<StorageOptions> options, CancellationToken ct)
    {
        long used = await db.Quotas.SumAsync(q => q.UsedBytes, ct);
        long limit = await db.Quotas.SumAsync(q => q.LimitBytes, ct);
        long blobs = await db.Blobs.LongCountAsync(b => b.Status == BlobStatus.Uploaded || b.Status == BlobStatus.Claimed, ct);
        return Results.Ok(new AdminStorageSummary(used, limit, blobs, options.Value.DefaultQuotaBytes));
    }

    /// <summary><c>?userIds=a,b,c</c> (tối đa 100). Người dùng chưa có dòng quota ⇒ hạn mức mặc định, đã dùng 0.</summary>
    private static async Task<IResult> ListAsync(string? userIds, StorageDbContext db, IOptions<StorageOptions> options, CancellationToken ct)
    {
        Guid[] ids = (userIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Guid.TryParse(v, out Guid id) ? id : Guid.Empty)
            .ToArray();
        if (ids.Length > 100 || ids.Contains(Guid.Empty))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "userIds must be up to 100 valid ids.");
        }

        Dictionary<Guid, Quota> found = await db.Quotas.AsNoTracking().Where(q => ids.Contains(q.UserId)).ToDictionaryAsync(q => q.UserId, ct);
        return Results.Ok(ids.Distinct().Select(id => found.TryGetValue(id, out Quota? q)
            ? new AdminQuota(id, q.LimitBytes, q.UsedBytes, q.ReservedBytes)
            : new AdminQuota(id, options.Value.DefaultQuotaBytes, 0, 0)).ToArray());
    }

    private static async Task<IResult> SetAsync(
        Guid userId,
        SetQuotaRequest request,
        StorageDbContext db,
        QuotaStore quotas,
        ICurrentUser admin,
        ILogger<AdminEndpoints> logger,
        CancellationToken ct)
    {
        if (request.LimitBytes is < 0 or > MaxQuotaBytes)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "limitBytes is out of range.");
        }

        await quotas.EnsureAsync(db, userId, ct);
        await db.Quotas.Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.LimitBytes, request.LimitBytes), ct);
        Quota quota = await db.Quotas.AsNoTracking().SingleAsync(q => q.UserId == userId, ct);
        StorageAdminLog.QuotaChanged(logger, admin.GetRequiredUserId(), userId, request.LimitBytes);
        return Results.Ok(new AdminQuota(userId, quota.LimitBytes, quota.UsedBytes, quota.ReservedBytes));
    }
}

internal static partial class StorageAdminLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin {AdminId} set the quota of {UserId} to {LimitBytes} bytes")]
    public static partial void QuotaChanged(ILogger logger, Guid adminId, Guid userId, long limitBytes);
}
