using System.Reflection;
using Drive.Application.Abstractions;
using Drive.Application.Items;
using Microsoft.Extensions.DependencyInjection;

namespace Drive.Application;

public static class DriveApplication
{
    public static Assembly Assembly => typeof(DriveApplication).Assembly;

    /// <summary>Dịch vụ dùng chung của tầng Application (MediatR + validator đăng ký qua AddApplicationDefaults).</summary>
    public static IServiceCollection AddDriveApplication(this IServiceCollection services)
    {
        services.AddScoped<IItemAuthorizer, OwnerOnlyItemAuthorizer>(); // Phase 4: thay bằng authorizer gọi Sharing
        services.AddScoped<ItemAccess>();
        services.AddScoped<UniqueNames>();
        return services;
    }
}
