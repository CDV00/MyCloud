using MyCloud.BuildingBlocks.Web;

// Service khung (Phase 0). Trách nhiệm & kế hoạch: docs/03-dac-ta-service.md §5, docs/07 Phase 4.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();

await app.RunAsync();

public partial class Program;
