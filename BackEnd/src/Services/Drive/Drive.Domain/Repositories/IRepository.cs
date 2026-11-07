using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Repositories;

public interface IRepository<TAggregate, in TKey>
    where TAggregate : AggregateRoot<TKey>
    where TKey : notnull
{
    Task<TAggregate?> FindByIdAsync(TKey id, CancellationToken cancellationToken = default);

    void Add(TAggregate aggregate);

    void Remove(TAggregate aggregate);
}

public interface IFolderRepository : IRepository<Entities.Folder, Guid>
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> HasChildrenAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IFileRepository : IRepository<Entities.File, Guid>;
