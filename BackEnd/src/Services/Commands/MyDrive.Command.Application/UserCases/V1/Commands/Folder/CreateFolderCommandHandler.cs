using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MyDrive.Command.Domain.Abstractions.Repositories;

namespace MyDrive.Command.Application.UserCases.V1.Commands.Folder;

public sealed class CreateFolderCommandHandler : ICommandHandler<BuidingBlock.Contract.Services.V1.Folder.Command.CreateFolderCommand>
{
    private readonly IRepositoryBase<Domain.Entities.Folder, Guid> _fileRepository;

    public CreateFolderCommandHandler(IRepositoryBase<Domain.Entities.Folder, Guid> fileRepository)
    {
        _fileRepository = fileRepository;
    }

    public async Task<Result> Handle(BuidingBlock.Contract.Services.V1.Folder.Command.CreateFolderCommand request, CancellationToken cancellationToken)
    {
        var file = Domain.Entities.Folder.CreateFolder(Guid.NewGuid(), request.Name, request.Description, request.parentId);
        _fileRepository.Add(file);

        return Result.Success();
    }
}