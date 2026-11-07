using MyCloud.BuildingBlocks.Authentication;
using MyCloud.Gateway;
using Yarp.ReverseProxy.Transforms;

// API Gateway (YARP): điểm vào duy nhất của client — docs/03 §1.
// Xác thực JWT (service vẫn tự validate lại), rate limit, correlation id, lọc header nội bộ, giới hạn body.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddJwtAuthentication(); // route không khai báo AuthorizationPolicy ⇒ yêu cầu đăng nhập (fallback policy)
builder.AddGatewayRateLimiting();

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = GatewayLimits.DefaultMaxRequestBodyBytes);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver() // "http://drive" → địa chỉ thật (Aspire/Kubernetes)
    .AddTransforms(context => context.AddRequestTransform(InternalHeaders.RemoveFromClientRequest));

string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins) // không dùng "*"; web app chạy cùng origin nên thường không cần CORS
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders(CorrelationId.HeaderName)));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter(); // sau authentication: giới hạn theo user cho API thường
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapReverseProxy();

await app.RunAsync();

public partial class Program;
