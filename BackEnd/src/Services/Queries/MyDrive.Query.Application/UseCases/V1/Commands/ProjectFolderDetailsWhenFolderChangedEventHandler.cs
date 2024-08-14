using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;

namespace MyDrive.Query.Application.UseCases.V1.Commands;

internal class ProjectFolderDetailsWhenFolderChangedEventHandler
    : ICommandHandler<DomainEvent.FolderCreated>,
    ICommandHandler<DomainEvent.FolderDeleted>,
    ICommandHandler<DomainEvent.FolderUpdated>
{
    // Repository working with MongoDB
    private readonly IMongoRepository<FolderProjection> _folderRepository;

    public ProjectFolderDetailsWhenFolderChangedEventHandler(IMongoRepository<FolderProjection> folderRepository)
    {
        _folderRepository = folderRepository;
    }

    public async Task<Result> Handle(DomainEvent.FolderCreated request, CancellationToken cancellationToken)
    {
        var folder = new FolderProjection()
        {
            DocumentId = request.Id,
            Name = request.Name,
            Description = request.Description,
            ParentId = request.parentId
        };
        // Create new a folder
        await _folderRepository.InsertOneAsync(folder);

        return Result.Success();
    }

    public async Task<Result> Handle(DomainEvent.FolderDeleted request, CancellationToken cancellationToken)
    {
        // Find and delete folder
        var folder = await _folderRepository.FindOneAsync(p => p.DocumentId == request.Id)
            ?? throw new ArgumentNullException();

        await _folderRepository.DeleteOneAsync(p => p.DocumentId == request.Id);

        return Result.Success();
    }

    public async Task<Result> Handle(DomainEvent.FolderUpdated request, CancellationToken cancellationToken)
    {
        // Find and update folder
        var folder = await _folderRepository.FindOneAsync(p => p.DocumentId == request.Id)
           ?? throw new ArgumentNullException();

        folder.Name = request.Name;
        folder.Description = request.Description;
        folder.ModifiedOnUtc = DateTime.UtcNow;
        folder.ParentId = request.parentId;

        await _folderRepository.ReplaceOneAsync(folder);

        return Result.Success();
    }

}
