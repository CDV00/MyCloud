using Drive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Drive.Infrastructure.Maintenance;

/// <summary>
/// Xoá bản ghi upload đã quá hạn (Storage tự huỷ multipart và trả quota) hoặc đã hoàn tất từ lâu.
/// Giữ thêm 1 ngày sau hạn để lệnh "hoàn tất" gửi lặp vẫn nhận được kết quả.
/// </summary>
internal sealed partial class PendingUploadCleanup(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PendingUploadCleanup> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DriveDbContext>();
                DateTimeOffset cutoff = timeProvider.GetUtcNow() - Grace;
                int deleted = await db.PendingUploads.Where(u => u.ExpiresAt < cutoff).ExecuteDeleteAsync(stoppingToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired upload records")]
    private partial void LogDeleted(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up expired uploads failed")]
    private partial void LogFailed(Exception exception);
}
