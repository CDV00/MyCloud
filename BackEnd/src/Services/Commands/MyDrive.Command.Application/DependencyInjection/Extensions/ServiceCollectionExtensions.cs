using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MyDrive.Command.Application.Behaviors;
using MyDrive.Command.Application.Mapper;

namespace MyDrive.Command.Application.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediatRApplication(this IServiceCollection services)
        => services.AddMediatR(cfg=>
        cfg.RegisterServicesFromAssemblies(AssemblyReference.Assembly))
        .AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>))
        .AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformancePipelineBehavior<,>))
        .AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionPipelineBehavior<,>))
        .AddValidatorsFromAssembly(BuidingBlock.Contract.AssemblyReference.Assembly, includeInternalTypes: true);

    //Add Automapper
    public static IServiceCollection AddAutoMapperApplication(this IServiceCollection services)
        => services.AddAutoMapper(typeof(ServiceProfile));
}
