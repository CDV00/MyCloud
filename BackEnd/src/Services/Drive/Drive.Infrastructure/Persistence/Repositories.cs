using Drive.Domain.Entities;
using Drive.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Domain.Primitives;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Infrastructure.Persistence;

internal class Repository<TAggregate>(DriveDbContext db) : IRepository<TAggregate, Guid>
    where TAggregate : AggregateRoot<Guid>
{
    protected DriveDbContext Db { get; } = db;

    public Task<TAggregate?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Db.Set<TAggregate>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(TAggregate aggregate) => Db.Set<TAggregate>().Add(aggregate);

    public void Remove(TAggregate aggregate) => Db.Set<TAggregate>().Remove(aggregate);
}

internal sealed class FolderRepository(DriveDbContext db) : Repository<Folder>(db), IFolderRepository
{
    public Task<Folder?> FindOwnedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        Db.Folders.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);

    public Task<bool> ExistsOwnedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        Db.Folders.AnyAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);

    public async Task<bool> HasChildrenAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Db.Folders.AnyAsync(x => x.ParentId == id, cancellationToken)
        || await Db.Files.AnyAsync(x => x.FolderId == id, cancellationToken);
}

internal sealed class FileRepository(DriveDbContext db) : Repository<DriveFile>(db), IFileRepository
{
    public Task<DriveFile?> FindOwnedAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        Db.Files.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
}
