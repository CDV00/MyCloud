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

/// <summary>
/// Truy vấn theo chủ sở hữu: item của người khác được coi như không tồn tại (404, không phải 403) để không lộ id hợp lệ.
/// </summary>
public interface IFolderRepository : IRepository<Entities.Folder, Guid>
{
    Task<Entities.Folder?> FindOwnedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);

    Task<bool> ExistsOwnedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);

    Task<bool> HasChildrenAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IFileRepository : IRepository<Entities.File, Guid>
{
    Task<Entities.File?> FindOwnedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);
}
