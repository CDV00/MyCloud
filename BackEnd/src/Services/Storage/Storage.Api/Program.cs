using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Grpc;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.BuildingBlocks.Web;
using Storage.Api;
using Storage.Api.Data;
using Storage.Api.Grpc;
using Storage.Api.Maintenance;
using Storage.Api.ObjectStorage;
using Storage.Api.Uploads;

// Storage service: byte của file (S3/MinIO) và dung lượng — docs/03 §4, ADR-002, ADR-006, ADR-011.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);

builder.AddNpgsqlDbContext<StorageDbContext>("storagedb",
    configureDbContextOptions: o => o.UseSnakeCaseNamingConvention());
builder.AddMessaging(x =>
{
    x.AddPostgresOutbox<StorageDbContext>();
    x.AddConsumers(typeof(Program).Assembly);
});

builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(_ => ObjectStorageConnection.Parse(
    builder.Configuration.GetConnectionString("minio")
    ?? throw new InvalidOperationException("Connection string 'minio' is missing.")));
builder.Services.AddSingleton<S3ObjectStorage>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<QuotaStore>();
builder.Services.AddScoped<UploadService>();
builder.Services.AddScoped<Storage.Api.Messaging.BlobReclaimer>();
builder.Services.AddSingleton<StorageMaintenance>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StorageMaintenance>());

builder.Services.AddGrpcServerDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();
app.MapGrpcService<StorageGrpcService>(); // endpoint "grpc" (HTTP/2) — chỉ dùng nội bộ, không đi qua Gateway

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Database.MigrateAsync();
}

if (!await app.Services.GetRequiredService<S3ObjectStorage>().EnsureBucketAsync(CancellationToken.None))
{
    app.Logger.LogWarning("Object storage rejected the lifecycle rule; incomplete multipart uploads rely on the maintenance job only");
}

await app.RunAsync();

public partial class Program;
