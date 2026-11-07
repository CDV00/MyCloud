using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.BuildingBlocks.Web;
using Notification.Api.Data;
using Notification.Api.Delivery;
using Notification.Api.Notifications;

// Notification service — docs/03 §7: thông báo trong app (REST + SignalR), email (SMTP; Mailpit ở dev).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);

builder.AddNpgsqlDbContext<NotificationDbContext>("notificationdb", configureDbContextOptions: o => o.UseSnakeCaseNamingConvention());
builder.AddMessaging(x =>
{
    x.AddPostgresOutbox<NotificationDbContext>();
    x.AddConsumer<NotificationConsumers>();
    x.AddConsumer<PushNotificationConsumer>();
    x.AddConsumer<SendEmailConsumer>();
    x.AddConsumer<ItemRoomBroadcaster>();
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<NotificationQueries>();
builder.Services.AddOptions<CollaborationOptions>().Bind(builder.Configuration.GetSection(CollaborationOptions.SectionName));
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .PostConfigure(o => o.UseConnectionString(builder.Configuration.GetConnectionString("mailpit")));

// SignalR: nhiều instance dùng chung Redis backplane để đẩy tới tab đang nối với instance khác.
ISignalRServerBuilder signalR = builder.Services.AddSignalR();
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 } redis)
{
    signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("mycloud-notifications"));
}

builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();
app.MapHub<NotificationHub>(NotificationHub.Path);

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
