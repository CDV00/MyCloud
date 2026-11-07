using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Infrastructure.Maintenance;
using Drive.Infrastructure.Messaging;
using Drive.Infrastructure.Persistence;
using Drive.Infrastructure.Sharing;
using Drive.Infrastructure.Storage;
using Drive.Infrastructure.Trash;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.BuildingBlocks.Grpc;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.Contracts.Identity.V1;
using MyCloud.Contracts.Sharing.V1;
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

        // gRPC nội bộ (endpoint tên "grpc", HTTP/2 không TLS) bằng định danh service của Drive (ADR-014).
        builder.AddServiceIdentity();
        Uri storageGrpc = new(builder.Configuration["Services:Storage:Grpc"] ?? "http://_grpc.storage");
        services.AddGrpcServiceClient<StorageService.StorageServiceClient>(storageGrpc);
        services.AddScoped<IStorageClient, GrpcStorageClient>();

        Uri sharingGrpc = new(builder.Configuration["Services:Sharing:Grpc"] ?? "http://_grpc.sharing");
        services.AddGrpcServiceClient<SharingService.SharingServiceClient>(sharingGrpc);
        services.AddScoped<ISharingClient, GrpcSharingClient>();
        services.AddScoped<IItemAuthorizer, SharingItemAuthorizer>();

        Uri identityGrpc = new(builder.Configuration["Services:Identity:Grpc"] ?? "http://_grpc.identity");
        services.AddGrpcServiceClient<UserService.UserServiceClient>(identityGrpc);
        services.AddScoped<IUserDirectory, GrpcUserDirectory>();

        return builder;
    }
}
