using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.File;
using MyDrive.Command.Domain.Abstractions.Repositories;
using MyDriver.BuildBlock.FTP.Service;

namespace MyDrive.Command.Application.UserCases.V1.Commands.File;

public sealed class CreateFileCommandHandler : ICommandHandler<BuidingBlock.Contract.Services.V1.File.Command.CreateFileCommand>
{
    private readonly IRepositoryBase<Domain.Entities.File, Guid> _fileRepository;

    public CreateFileCommandHandler(IRepositoryBase<Domain.Entities.File, Guid> fileRepository)
    {
        _fileRepository = fileRepository;
    }

    public async Task<Result> Handle(BuidingBlock.Contract.Services.V1.File.Command.CreateFileCommand request, CancellationToken cancellationToken)
    {
        string storedName = await FileService.WriteFile(request.FileString, request.Name, request.type);
        var file = Domain.Entities.File.CreateFile(Guid.NewGuid(), request.Name, storedName, request.type, request.size, request.description, request.FolderId);
        _fileRepository.Add(file);

        return Result.Success();
    }
}
public sealed class CreateFile2CommandHandler : ICommandHandler<BuidingBlock.Contract.Services.V1.File.Command.CreateFile2Command>
{
    private readonly IRepositoryBase<Domain.Entities.File, Guid> _fileRepository;

    public CreateFile2CommandHandler(IRepositoryBase<Domain.Entities.File, Guid> fileRepository)
    {
        _fileRepository = fileRepository;
    }

    public async Task<Result> Handle(BuidingBlock.Contract.Services.V1.File.Command.CreateFile2Command request, CancellationToken cancellationToken)
    {

        try
        {
            //add file store
            var fileDto = await FileService.WriteFile(request.file);

            var file = Domain.Entities.File.CreateFile(Guid.NewGuid(), fileDto.Name, fileDto.StoredName, fileDto.Type, fileDto.Size, "", request.FolderId);
            _fileRepository.Add(file);

            return Result.Success();
        }
        catch (Exception ex)
        {

            throw;
        }

        
    }
}
