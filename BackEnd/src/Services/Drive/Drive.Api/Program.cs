using Drive.Application;
using Drive.Infrastructure;
using Drive.Infrastructure.Persistence;
using MyCloud.BuildingBlocks.Application;
using MyCloud.BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebApiDefaults(typeof(Program).Assembly);
builder.Services.AddApplicationDefaults(DriveApplication.Assembly);
builder.Services.AddDriveApplication();
builder.AddDriveInfrastructure();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapWebApiDefaults();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyDriveMigrationsAsync();
}

await app.RunAsync();

public partial class Program;
