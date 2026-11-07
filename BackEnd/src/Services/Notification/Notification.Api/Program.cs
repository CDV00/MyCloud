using MassTransit;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.BuildingBlocks.Web;

// Service khung (Phase 0). Trách nhiệm & kế hoạch: docs/03-dac-ta-service.md §7, docs/07 Phase 4.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);
builder.AddMessaging(x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();

await app.RunAsync();

public partial class Program;
