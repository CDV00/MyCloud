using Drive.Domain.Entities;
using Drive.Domain.Exceptions;
using Drive.Domain.Repositories;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Folders;

public sealed record DeleteFolderCommand(Guid Id) : ICommand;

internal sealed class DeleteFolderCommandHandler(IFolderRepository folders, ICurrentUser currentUser) : ICommandHandler<DeleteFolderCommand>
{
    public async Task<Result> Handle(DeleteFolderCommand request, CancellationToken cancellationToken)
    {
        Folder folder = await folders.FindOwnedAsync(request.Id, currentUser.GetRequiredUserId(), cancellationToken)
            ?? throw new FolderException.NotFound(request.Id);

        // Bản cũ xoá thư mục bỏ mặc thư mục/file con (lỗi S10). Tạm thời chỉ cho xoá thư mục rỗng;
        // xoá cả cây con qua thùng rác được làm ở Phase 3.
        if (await folders.HasChildrenAsync(folder.Id, cancellationToken))
        {
            throw new FolderException.NotEmpty(folder.Id);
        }

        folder.Delete();
        folders.Remove(folder);
        return Result.Success();
    }
}
