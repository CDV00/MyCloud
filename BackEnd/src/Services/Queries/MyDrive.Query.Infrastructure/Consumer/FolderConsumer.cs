using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MediatR;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;
using MyDrive.Query.Infrastructure.Abstractions;

namespace MyDrive.Query.Infrastructure.Consumer;

public static class FolderConsumer
{
    public class FolderCreatedConsumer : Consumer<DomainEvent.FolderCreated>
    {
        public FolderCreatedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }

    public class FolderDeletedConsumer : Consumer<DomainEvent.FolderDeleted>
    {
        public FolderDeletedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }

    public class FolderUpdatedConsumer : Consumer<DomainEvent.FolderUpdated>
    {
        public FolderUpdatedConsumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
            : base(sender, eventRepository)
        {
        }
    }
}
