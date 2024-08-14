using Auth.Api.Database;
using Auth.Api.Database.Models;
using Auth.Api.DependencyInjection.Extensions;
using Auth.Api.DependencyInjection.Options;
using Auth.Api.Persistence.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.ConfigureSqlServerRetryOptions(builder.Configuration.GetSection(nameof(SqlServerRetryOptions)));
builder.Services.AddSqlServer();
builder.Services.AddRepository();
builder.Services.AddService();
builder.Services.AddAutoMapper();


builder.Services.Configure<IdentityOptions>(options =>
{
    // Default Lockout settings.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
});

// Add Swagger
builder.Services
        .AddSwaggerGenNewtonsoftSupport()
        .AddEndpointsApiExplorer()
        .AddSwaggerAPI();

var app = builder.Build();

app.MapControllers();

// Configure the HTTP request pipeline. 
if (builder.Environment.IsDevelopment() || builder.Environment.IsStaging())
    app.UseSwaggerAPI(); // => After MapCarter => Show Version

app.Run();
