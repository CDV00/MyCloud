using MassTransit;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.BuildingBlocks.Web;
using Search.Api.ReadModel;

SearchDb.RegisterSerializers();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);

// Aspire: IMongoClient + IMongoDatabase ("searchdb"), health check, OpenTelemetry.
builder.AddMongoDBClient("searchdb");
builder.Services.AddSingleton<SearchDb>();

builder.AddMessaging(x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();

await app.Services.GetRequiredService<SearchDb>().EnsureIndexesAsync();

await app.RunAsync();

public partial class Program;
