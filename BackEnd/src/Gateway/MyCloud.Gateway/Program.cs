// API Gateway (YARP): điểm vào duy nhất của client.
// Phase 0: routing + service discovery + CORS. Phase 1: xác thực JWT, rate limit, correlation id (docs/03 §1).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver(); // "http://drive" → địa chỉ thật (Aspire/Kubernetes)

string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins) // không dùng "*" như bản cũ
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.MapDefaultEndpoints();
app.MapReverseProxy();

await app.RunAsync();
