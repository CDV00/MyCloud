using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Drive.Infrastructure.Persistence;

/// <summary>Chỉ dùng cho `dotnet ef migrations add` — không kết nối DB thật.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DriveDbContext>
{
    public DriveDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DriveDbContext>()
            .UseNpgsql("Host=localhost;Database=drive")
            .UseSnakeCaseNamingConvention()
            .Options);
}
