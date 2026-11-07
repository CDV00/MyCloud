using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Grpc;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.BuildingBlocks.Web;
using Sharing.Api.Access;
using Sharing.Api.Data;
using Sharing.Api.Grpc;

// Sharing service: ai được làm gì với item nào + chia sẻ — docs/03 §5, ADR-014.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);

builder.AddNpgsqlDbContext<SharingDbContext>("sharingdb", configureDbContextOptions: o => o.UseSnakeCaseNamingConvention());
builder.AddMessaging(x =>
{
    x.AddPostgresOutbox<SharingDbContext>();
    x.AddConsumer<ItemsPurgedConsumer>();
});

builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AccessService>();
builder.Services.AddGrpcServerDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();
app.MapGrpcService<SharingGrpcService>(); // endpoint "grpc" (HTTP/2) — chỉ nội bộ, chỉ service

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<SharingDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
