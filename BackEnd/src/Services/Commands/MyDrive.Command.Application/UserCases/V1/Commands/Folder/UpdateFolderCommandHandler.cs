using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MyDrive.Command.Domain.Abstractions.Repositories;
using MyDrive.Command.Domain.Exceptions;

namespace MyDrive.Command.Application.UserCases.V1.Commands.Folder;

public sealed class UpdateFolderCommandHandler : ICommandHandler<BuidingBlock.Contract.Services.V1.Folder.Command.UpdateFolderCommand>
{
    private readonly IRepositoryBase<Domain.Entities.Folder, Guid> _folderRepository;

    public UpdateFolderCommandHandler(IRepositoryBase<Domain.Entities.Folder, Guid> folderRepository)
    {
        _folderRepository = folderRepository;
    }

    public async Task<Result> Handle(BuidingBlock.Contract.Services.V1.Folder.Command.UpdateFolderCommand request, CancellationToken cancellationToken)
    {
        var folder = await _folderRepository.FindByIdAsync(request.Id) ?? throw new FolderException.FolderNotFoundException(request.Id); // Solution 1
        folder.Update(request.Name, request.Description, request.parentId);

        return Result.Success();
    }
}