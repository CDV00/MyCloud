using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.File;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;

namespace MyDrive.Query.Application.UseCases.V1.Commands;

internal class ProjectFileDetailsWhenFileChangedEventHandler
    : ICommandHandler<DomainEvent.FileCreated>,
    ICommandHandler<DomainEvent.FileDeleted>,
    ICommandHandler<DomainEvent.FileUpdated>
{
    // Repository working with MongoDB
    private readonly IMongoRepository<FileProjection> _FileRepository;

    public ProjectFileDetailsWhenFileChangedEventHandler(IMongoRepository<FileProjection> FileRepository)
    {
        _FileRepository = FileRepository;
    }

    public async Task<Result> Handle(DomainEvent.FileCreated request, CancellationToken cancellationToken)
    {
        var File = new FileProjection()
        {
            DocumentId = request.Id,
            Name = request.Name,
            StoredName = request.StoredName,
            Type = request.Type,
            Size = request.Size,
            Description = request.Description,
            FolderId = request.FolderId
        };
        // Create new a File
        await _FileRepository.InsertOneAsync(File);

        return Result.Success();
    }

    public async Task<Result> Handle(DomainEvent.FileDeleted request, CancellationToken cancellationToken)
    {
        // Find and delete File
        var File = await _FileRepository.FindOneAsync(p => p.DocumentId == request.Id)
            ?? throw new ArgumentNullException();

        await _FileRepository.DeleteOneAsync(p => p.DocumentId == request.Id);

        return Result.Success();
    }

    public async Task<Result> Handle(DomainEvent.FileUpdated request, CancellationToken cancellationToken)
    {
        // Find and update File
        var File = await _FileRepository.FindOneAsync(p => p.DocumentId == request.Id)
           ?? throw new ArgumentNullException();

        File.Name = request.Name;
        File.Description = request.Description;
        File.ModifiedOnUtc = DateTime.UtcNow;
        File.FolderId = request.FolderId;

        await _FileRepository.ReplaceOneAsync(File);

        return Result.Success();
    }

}