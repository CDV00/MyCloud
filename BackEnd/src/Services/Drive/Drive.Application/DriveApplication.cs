using System.Reflection;
using Drive.Application.Abstractions;
using Drive.Application.Items;
using Drive.Application.Versions;
using Microsoft.Extensions.DependencyInjection;

namespace Drive.Application;

public static class DriveApplication
{
    public static Assembly Assembly => typeof(DriveApplication).Assembly;

    /// <summary>Dịch vụ dùng chung của tầng Application (MediatR + validator đăng ký qua AddApplicationDefaults).</summary>
    public static IServiceCollection AddDriveApplication(this IServiceCollection services)
    {
        services.AddScoped<LinkContext>();
        services.AddScoped<ItemAccess>();
        services.AddScoped<UniqueNames>();
        services.AddScoped<ItemDtos>();
        services.AddScoped<VersionRetention>();
        return services;
    }
}
