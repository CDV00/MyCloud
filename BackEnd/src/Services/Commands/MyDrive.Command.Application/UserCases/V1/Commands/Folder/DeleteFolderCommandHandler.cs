using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MyDrive.Command.Domain.Abstractions.Repositories;
using MyDrive.Command.Domain.Exceptions;

namespace MyDrive.Command.Application.UserCases.V1.Commands.Folder;

public sealed class DeleteFolderCommandHandler : ICommandHandler<BuidingBlock.Contract.Services.V1.Folder.Command.DeleteFolderCommand>
{
    private readonly IRepositoryBase<Domain.Entities.Folder, Guid> _folderRepository;

    public DeleteFolderCommandHandler(IRepositoryBase<Domain.Entities.Folder, Guid> folderRepository)
    {
        _folderRepository = folderRepository;
    }

    public async Task<Result> Handle(BuidingBlock.Contract.Services.V1.Folder.Command.DeleteFolderCommand request, CancellationToken cancellationToken)
    {
        var folder = await _folderRepository.FindByIdAsync(request.Id) ?? throw new FolderException.FolderNotFoundException(request.Id);
        folder.Delete();
        _folderRepository.Remove(folder);

        return Result.Success();
    }
}