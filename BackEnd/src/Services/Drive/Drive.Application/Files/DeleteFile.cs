using Drive.Application.Abstractions;
using Drive.Domain.Exceptions;
using Drive.Domain.Repositories;
using Microsoft.Extensions.Logging;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Application.Files;

public sealed record DeleteFileCommand(Guid Id) : ICommand;

internal sealed class DeleteFileCommandHandler(
    IFileRepository files,
    IFileContentStore contentStore,
    IUnitOfWork unitOfWork,
    ILogger<DeleteFileCommandHandler> logger) : ICommandHandler<DeleteFileCommand>
{
    public async Task<Result> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        DriveFile file = await files.FindByIdAsync(request.Id, cancellationToken)
            ?? throw new FileException.NotFound(request.Id);

        file.Delete();
        files.Remove(file);

        // Commit metadata trước, rồi mới xoá nội dung: lỗi ở bước sau chỉ để lại rác, không làm mất dữ liệu.
        // Phase 3: xoá mềm (thùng rác) + Storage service dọn nội dung bất đồng bộ qua event ItemsPurged.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await contentStore.DeleteAsync(file.StorageKey, cancellationToken);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete content {StorageKey} of file {FileId}", file.StorageKey, file.Id);
        }

        return Result.Success();
    }
}
