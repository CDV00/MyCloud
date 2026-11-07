using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drive.Infrastructure.Persistence;

public static class MigrationExtensions
{
    /// <summary>
    /// Chỉ dùng cho môi trường dev/test. Production chạy migration bằng job riêng trước khi rollout (docs/06 §7).
    /// </summary>
    public static async Task ApplyDriveMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        DriveDbContext db = scope.ServiceProvider.GetRequiredService<DriveDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
