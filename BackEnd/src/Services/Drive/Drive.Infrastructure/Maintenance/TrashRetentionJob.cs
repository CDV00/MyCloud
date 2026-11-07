using Drive.Application.Abstractions;
using Drive.Domain.Items;
using Drive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Drive.Infrastructure.Maintenance;

/// <summary>
/// Xoá vĩnh viễn item nằm trong thùng rác quá <see cref="DriveOptions.TrashRetention"/> (mặc định 30 ngày).
/// Nhiều instance chạy cùng lúc không gây sai: lô đã bị instance khác xoá đơn giản là không còn để xoá.
/// </summary>
public sealed partial class TrashRetentionJob(
    IServiceScopeFactory scopeFactory,
    DriveOptions options,
    TimeProvider timeProvider,
    ILogger<TrashRetentionJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int GroupsPerRun = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Trả số item đã xoá vĩnh viễn.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DriveDbContext>();
        var purger = scope.ServiceProvider.GetRequiredService<ITrashPurger>();
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - options.TrashRetention;

        List<DriveItem> expired = await db.Items.AsNoTracking()
            .Where(x => x.TrashRootId == x.Id && x.TrashedAt < cutoff)
            .OrderBy(x => x.TrashedAt)
            .Take(GroupsPerRun)
            .ToListAsync(ct);

        int purged = 0;
        foreach (DriveItem root in expired)
        {
            purged += await purger.PurgeAsync(root, ct);
        }

        return purged;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Purging expired trash failed")]
    private partial void LogFailed(Exception exception);
}
