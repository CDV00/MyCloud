using FluentValidation;
using Identity.Api.Auth;
using Identity.Api.Data;
using Identity.Api.Keys;
using Identity.Api.Services;
using Identity.Api.Tokens;
using Identity.Api.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Grpc;
using MyCloud.BuildingBlocks.Messaging;
using MyCloud.BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);

builder.AddNpgsqlDbContext<AppIdentityDbContext>("identitydb",
    configureDbContextOptions: o => o.UseSnakeCaseNamingConvention());
builder.AddMessaging(x => x.AddPostgresOutbox<AppIdentityDbContext>());

builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppIdentityDbContext>();

builder.AddTokenServices();
builder.Services.AddOptions<ServiceClientOptions>().Bind(builder.Configuration.GetSection(ServiceClientOptions.SectionName));
builder.Services.AddOptions<AdminOptions>().Bind(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddSingleton<AdminBootstrap>();
builder.Services.AddGrpcServerDefaults();
builder.Services.AddValidatorsFromAssemblyContaining<AuthEndpoints>(includeInternalTypes: true);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();
app.MapServiceClientEndpoints();          // POST /connect/token: client credentials cho service (ADR-014)
app.MapGrpcService<UserGrpcService>();    // endpoint "grpc" (HTTP/2), chỉ nội bộ

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();
}

// Nạp (hoặc tạo lần đầu) khoá ký trước khi nhận request.
await app.Services.GetRequiredService<SigningKeyStore>().ReloadAsync(CancellationToken.None);

await app.RunAsync();

public partial class Program;
