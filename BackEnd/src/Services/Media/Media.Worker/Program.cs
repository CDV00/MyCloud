using MyCloud.BuildingBlocks.Messaging;

// Worker khung (Phase 0): kết nối RabbitMQ, chưa có consumer.
// Phase 5 thêm consumer FileVersionAdded → sinh thumbnail → publish ThumbnailGenerated.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddMessaging();

var host = builder.Build();
await host.RunAsync();
