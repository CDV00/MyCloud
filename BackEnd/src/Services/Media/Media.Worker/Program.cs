using MassTransit;
using Media.Worker.Thumbnails;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.BuildingBlocks.Grpc;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.Contracts.Storage.V1;

// Media Worker (docs/03 §8, ADR-015): FileVersionAdded → thumbnail WebP → Storage (gRPC) → ThumbnailGenerated.
// Không có REST; chỉ health check của ServiceDefaults.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

MediaOptions mediaOptions = builder.Configuration.GetSection(MediaOptions.SectionName).Get<MediaOptions>() ?? new MediaOptions();
builder.Services.AddSingleton(mediaOptions);
builder.Services.AddSingleton<VideoFrameExtractor>();

builder.AddMessaging(x => x.AddConsumer<MediaThumbnailConsumer, MediaThumbnailConsumerDefinition>());

// Đọc blob / ghi thumbnail qua Storage bằng định danh service của Media (client credentials — ADR-014).
builder.AddServiceIdentity();
Uri storageGrpc = new(builder.Configuration["Services:Storage:Grpc"] ?? "http://_grpc.storage");
builder.Services.AddGrpcServiceClient<StorageService.StorageServiceClient>(storageGrpc);
builder.Services.AddHttpClient(MediaThumbnailConsumer.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(2));

var host = builder.Build();
await host.RunAsync();
