using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.BuildingBlocks.Application.Behaviors;

namespace MyCloud.BuildingBlocks.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký MediatR + validator của assembly và pipeline chuẩn:
    /// Performance → Validation → UnitOfWork (chỉ khi useUnitOfWork = true).
    /// </summary>
    public static IServiceCollection AddApplicationDefaults(this IServiceCollection services, Assembly assembly, bool useUnitOfWork = true)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(PerformancePipelineBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
            if (useUnitOfWork)
            {
                cfg.AddOpenBehavior(typeof(UnitOfWorkPipelineBehavior<,>));
            }
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
