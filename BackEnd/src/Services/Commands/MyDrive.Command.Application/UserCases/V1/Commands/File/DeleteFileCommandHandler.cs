using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.Command.Domain.Abstractions.Repositories;

namespace MyDrive.Command.Application.UserCases.V1.Commands.File;

public sealed class DeleteFileCommandHandler : ICommandHandler<BuidingBlock.Contract.Services.V1.File.Command.DeleteFileCommand>
{
    private readonly IRepositoryBase<Domain.Entities.File, Guid> _fileRepository;

    public DeleteFileCommandHandler(IRepositoryBase<Domain.Entities.File, Guid> fileRepository)
    {
        _fileRepository = fileRepository;
    }

    public async Task<Result> Handle(BuidingBlock.Contract.Services.V1.File.Command.DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var file = await _fileRepository.FindByIdAsync(request.Id);// ?? throw new FileException.FileNotFoundException(request.Id);
        file.Delete();
        _fileRepository.Remove(file);

        return Result.Success();
    }
}