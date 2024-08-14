using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MediatR;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;
using MyDrive.Query.Infrastructure.Abstractions;

namespace MyDrive.Query.Infrastructure.Consumer;

public static class FolderConsumer
{
    public class ProductCreatedConsumer : Consumer<DomainEvent.FolderCreated>
    {
        public ProductCreatedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }

    public class ProductDeletedConsumer : Consumer<DomainEvent.FolderDeleted>
    {
        public ProductDeletedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }

    public class ProductUpdatedConsumer : Consumer<DomainEvent.FolderUpdated>
    {
        public ProductUpdatedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }
}
