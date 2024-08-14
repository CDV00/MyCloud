using MyDrive.Query.Domain.Abstractions.Options;

namespace MyDrive.Query.Infrastructure.DependencyInjection.Options;

public class MongoDbSettings : IMongoDbSettings
{
    public string DatabaseName { get; set; }
    public string ConnectionString { get; set; }
}
