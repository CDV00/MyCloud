using Drive.Domain.Exceptions;
using Drive.Domain.Repositories;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Application.Files;

/// <summary>Đổi tên, mô tả hoặc thư mục chứa. Không đổi nội dung file.</summary>
public sealed record UpdateFileCommand(Guid Id, string Name, string? Description, Guid FolderId) : ICommand;

internal sealed class UpdateFileCommandValidator : AbstractValidator<UpdateFileCommand>
{
    public UpdateFileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.FolderId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DriveFile.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(DriveFile.DescriptionMaxLength);
    }
}

internal sealed class UpdateFileCommandHandler(IFileRepository files, IFolderRepository folders) : ICommandHandler<UpdateFileCommand>
{
    public async Task<Result> Handle(UpdateFileCommand request, CancellationToken cancellationToken)
    {
        DriveFile file = await files.FindByIdAsync(request.Id, cancellationToken)
            ?? throw new FileException.NotFound(request.Id);

        if (file.FolderId != request.FolderId && !await folders.ExistsAsync(request.FolderId, cancellationToken))
        {
            throw new FolderException.NotFound(request.FolderId);
        }

        file.UpdateMetadata(request.Name, request.Description, request.FolderId);
        return Result.Success();
    }
}
