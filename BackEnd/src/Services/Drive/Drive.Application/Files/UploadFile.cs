using Drive.Application.Abstractions;
using Drive.Domain.Exceptions;
using Drive.Domain.Repositories;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Application.Files;

public sealed record UploadFileCommand(
    Guid FolderId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content,
    string? Description) : ICommand<Guid>;

internal sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    /// <summary>Giới hạn tạm thời khi file còn đi qua service. Phase 2 nâng lên 5 GB với presigned multipart upload.</summary>
    public const long MaxSizeBytes = 100L * 1024 * 1024;

    public UploadFileCommandValidator()
    {
        RuleFor(x => x.FolderId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(DriveFile.NameMaxLength);
        RuleFor(x => x.SizeBytes).GreaterThan(0).LessThanOrEqualTo(MaxSizeBytes);
        RuleFor(x => x.Description).MaximumLength(DriveFile.DescriptionMaxLength);
    }
}

internal sealed class UploadFileCommandHandler(IFolderRepository folders, IFileRepository files, IFileContentStore contentStore)
    : ICommandHandler<UploadFileCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        if (!await folders.ExistsAsync(request.FolderId, cancellationToken))
        {
            throw new FolderException.NotFound(request.FolderId);
        }

        // Nếu lưu DB thất bại sau bước này, nội dung thành "mồ côi" — chấp nhận ở Phase 0,
        // Phase 2 xử lý bằng trạng thái blob + GC của Storage service (ADR-006).
        string storageKey = await contentStore.SaveAsync(request.Content, cancellationToken);

        var file = DriveFile.Create(
            Guid.CreateVersion7(),
            Path.GetFileName(request.FileName),
            storageKey,
            request.ContentType,
            request.SizeBytes,
            request.Description,
            request.FolderId);

        files.Add(file);
        return file.Id;
    }
}
