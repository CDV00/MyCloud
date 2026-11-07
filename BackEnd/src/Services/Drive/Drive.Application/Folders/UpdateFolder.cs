using Drive.Domain.Entities;
using Drive.Domain.Exceptions;
using Drive.Domain.Repositories;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Folders;

public sealed record UpdateFolderCommand(Guid Id, string Name, string? Description, Guid? ParentId) : ICommand;

internal sealed class UpdateFolderCommandValidator : AbstractValidator<UpdateFolderCommand>
{
    public UpdateFolderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Folder.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Folder.DescriptionMaxLength);
    }
}

internal sealed class UpdateFolderCommandHandler(IFolderRepository folders, ICurrentUser currentUser) : ICommandHandler<UpdateFolderCommand>
{
    public async Task<Result> Handle(UpdateFolderCommand request, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.GetRequiredUserId();
        Folder folder = await folders.FindOwnedAsync(request.Id, ownerId, cancellationToken)
            ?? throw new FolderException.NotFound(request.Id);

        if (request.ParentId is { } parentId && parentId != folder.Id
            && !await folders.ExistsOwnedAsync(parentId, ownerId, cancellationToken))
        {
            throw new FolderException.NotFound(parentId);
        }

        // Chặn di chuyển thư mục vào chính con cháu của nó: làm ở Phase 2 bằng materialized path.
        folder.Update(request.Name, request.Description, request.ParentId);
        return Result.Success();
    }
}
