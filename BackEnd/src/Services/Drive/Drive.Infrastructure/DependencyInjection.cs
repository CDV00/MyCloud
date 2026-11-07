using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Infrastructure.Maintenance;
using Drive.Infrastructure.Messaging;
using Drive.Infrastructure.Persistence;
using Drive.Infrastructure.Storage;
using Drive.Infrastructure.Trash;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Grpc;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.Contracts.Storage.V1;

namespace Drive.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionName = "drivedb";

    public static IHostApplicationBuilder AddDriveInfrastructure(this IHostApplicationBuilder builder)
    {
        // Aspire: connection string "drivedb", health check, retry, OpenTelemetry.
        builder.AddNpgsqlDbContext<DriveDbContext>(ConnectionName,
            configureDbContextOptions: o => o.UseSnakeCaseNamingConvention());

        builder.AddMessaging(x =>
        {
            x.AddPostgresOutbox<DriveDbContext>();
            x.AddConsumer<UserRegisteredConsumer>();
        });

        IServiceCollection services = builder.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDriveItemRepository, DriveItemRepository>();
        services.AddScoped<IPendingUploadRepository, PendingUploadRepository>();
        services.AddScoped<IFileVersionRepository, FileVersionRepository>();
        services.AddScoped<IStarRepository, StarRepository>();
        services.AddScoped<ITrashPurger, TrashPurger>();
        services.AddScoped<IItemAccessLog, ItemAccessLog>();
        services.AddSingleton(builder.Configuration.GetSection(DriveOptions.SectionName).Get<DriveOptions>() ?? new DriveOptions());
        services.AddHostedService<PendingUploadCleanup>();
        services.AddSingleton<TrashRetentionJob>();
        services.AddHostedService(sp => sp.GetRequiredService<TrashRetentionJob>());

        // Storage gRPC: endpoint tên "grpc" của service "storage" (HTTP/2 không TLS, chỉ trong mạng nội bộ).
        Uri storageGrpc = new(builder.Configuration["Services:Storage:Grpc"] ?? "http://_grpc.storage");
        services.AddGrpcClientDefaults<StorageService.StorageServiceClient>(storageGrpc);
        services.AddScoped<IStorageClient, GrpcStorageClient>();

        return builder;
    }
}
