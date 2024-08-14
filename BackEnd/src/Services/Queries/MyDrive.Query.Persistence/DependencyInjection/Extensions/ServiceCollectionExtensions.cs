using Microsoft.Extensions.DependencyInjection;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Persistence.Repositories;

namespace MyDrive.Query.Persistence.DependencyInjection.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddServicesPersistence(this IServiceCollection services)
    {
        services.AddScoped(typeof(IMongoRepository<>), typeof(MongoRepository<>));
    }
}