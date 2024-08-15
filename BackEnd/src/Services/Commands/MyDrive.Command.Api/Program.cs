using Carter;
using MyDrive.Command.API.Middleware;
using MyDrive.Command.API.DependencyInjection.Extensions;
using MyDrive.Command.Application.DependencyInjection.Extensions;
using MyDrive.Command.Persistence.DependencyInjection.Extensions;
using MyDrive.Command.Persistence.DependencyInjection.Options;
using MicroElements.Swashbuckle.FluentValidation.AspNetCore;
using MyDrive.Command.Infrastructure.DependencyInjection.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

//Add services to the container.
//Add configuration

Log.Logger = new LoggerConfiguration().ReadFrom
    .Configuration(builder.Configuration)
    .CreateLogger();

builder.Logging
    .ClearProviders()
    .AddSerilog();

builder.Host.UseSerilog();

//Add configure cors
builder.Services.AddCors(p => p.AddPolicy("MyDrive.Command", build =>
{
    build.WithOrigins("*").AllowAnyMethod().AllowAnyHeader();
}));

// Add Carter module
builder.Services.AddCarter();

// Add Swagger
builder.Services
        .AddSwaggerGenNewtonsoftSupport()
        .AddFluentValidationRulesToSwagger()
        .AddEndpointsApiExplorer()
        .AddSwaggerAPI();

builder.Services
    .AddApiVersioning(options => options.ReportApiVersions = true)
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddMediatRApplication();
builder.Services.AddAutoMapperApplication();

// Configure masstransit rabbitmq
builder.Services.AddMasstransitRabbitMQInfrastructure(builder.Configuration);
builder.Services.AddQuartzInfrastructure();
//builder.Services.AddServicesInfrastructure();
builder.Services.AddRedisInfrastructure(builder.Configuration);
builder.Services.ConfigureServicesInfrastructure(builder.Configuration);

// Configure Options and SQL => Remember mapcarter
builder.Services.AddInterceptorPersistence();
builder.Services.ConfigureSqlServerRetryOptionsPersistence(builder.Configuration.GetSection(nameof(SqlServerRetryOptions)));
builder.Services.AddSqlServerPersistence();
builder.Services.AddRepositoryPersistence();

// Add Middleware => Remember using middleware
builder.Services.AddTransient<ExceptionHandlingMiddleware>();

var app = builder.Build();

app.UseCors("MyDrive.Command");

// Using middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.

//app.UseHttpsRedirection();

//app.UseAuthorization();

// Add API Endpoint with carter module
app.MapCarter();

// Configure the HTTP request pipeline. 
if (builder.Environment.IsDevelopment() || builder.Environment.IsStaging())
    app.UseSwaggerAPI(); // => After MapCarter => Show Version

try
{
    await app.RunAsync();
    Log.Information("Stopped cleanly");
}
catch (Exception ex)
{
    Log.Fatal(ex, "An unhandled exception occured during bootstrapping");
    await app.StopAsync();
}
finally
{
    Log.CloseAndFlush();
    await app.DisposeAsync();
}

public partial class Program { }
