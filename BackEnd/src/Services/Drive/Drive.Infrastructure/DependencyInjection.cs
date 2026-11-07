using Drive.Application.Abstractions;
using Drive.Domain.Repositories;
using Drive.Infrastructure.Persistence;
using Drive.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Messaging;

namespace Drive.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionName = "drivedb";

    public static IHostApplicationBuilder AddDriveInfrastructure(this IHostApplicationBuilder builder)
    {
        // Aspire: connection string "drivedb", health check, retry, OpenTelemetry.
        builder.AddNpgsqlDbContext<DriveDbContext>(ConnectionName,
            configureDbContextOptions: o => o.UseSnakeCaseNamingConvention());

        builder.AddMessaging(x => x.AddPostgresOutbox<DriveDbContext>());

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
        builder.Services.AddScoped<IFolderRepository, FolderRepository>();
        builder.Services.AddScoped<IFileRepository, FileRepository>();

        builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection(FileStorageOptions.SectionName));
        builder.Services.AddSingleton<IFileContentStore, LocalDiskFileContentStore>();

        return builder;
    }
}
