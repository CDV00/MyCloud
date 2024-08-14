using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.File;
using MyDrive.Command.Domain.Abstractions.Repositories;
using MyDrive.Command.Domain.Exceptions;

namespace MyDrive.Command.Application.UserCases.V1.Commands.File;

public sealed class UpdateFileCommandHandler : ICommandHandler<BuidingBlock.Contract.Services.V1.File.Command.UpdateFileCommand>
{
    private readonly IRepositoryBase<Domain.Entities.File, Guid> _fileRepository;

    public UpdateFileCommandHandler(IRepositoryBase<Domain.Entities.File, Guid> fileRepository)
    {
        _fileRepository = fileRepository;
    }

    public async Task<Result> Handle(BuidingBlock.Contract.Services.V1.File.Command.UpdateFileCommand request, CancellationToken cancellationToken)
    {
        string storedName = "";
        var file = await _fileRepository.FindByIdAsync(request.Id);// ?? throw new FileException.FileNotFoundException(request.Id); // Solution 1
        file.Update(request.Name, storedName, request.type, request.size, request.description, request.FolderId);

        return Result.Success();
    }
}