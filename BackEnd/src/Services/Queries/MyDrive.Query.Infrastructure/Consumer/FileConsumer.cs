using MyDrive.BuidingBlock.Contract.Services.V1.File;
using MediatR;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;
using MyDrive.Query.Infrastructure.Abstractions;

namespace MyDrive.Query.Infrastructure.Consumer;

public static class FileConsumer
{
    public class FileCreatedConsumer : Consumer<DomainEvent.FileCreated>
    {
        public FileCreatedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }

    public class FileDeletedConsumer : Consumer<DomainEvent.FileDeleted>
    {
        public FileDeletedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }

    public class FileUpdatedConsumer : Consumer<DomainEvent.FileUpdated>
    {
        public FileUpdatedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }
}
