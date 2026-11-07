using MyCloud.BuildingBlocks.Web;

// Service khung (Phase 0). Trách nhiệm & kế hoạch: docs/03-dac-ta-service.md §4, docs/07 Phase 2.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();

await app.RunAsync();

public partial class Program;
