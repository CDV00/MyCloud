using FluentValidation;
using Identity.Api.Auth;
using Identity.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddValidatorsFromAssemblyContaining<AuthEndpoints>(includeInternalTypes: true);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
