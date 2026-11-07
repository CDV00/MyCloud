using Drive.Domain.Entities;
using Drive.Domain.Exceptions;
using Drive.Domain.Repositories;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Folders;

public sealed record CreateFolderCommand(string Name, string? Description, Guid? ParentId) : ICommand<Guid>;

internal sealed class CreateFolderCommandValidator : AbstractValidator<CreateFolderCommand>
{
    public CreateFolderCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Folder.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Folder.DescriptionMaxLength);
    }
}

internal sealed class CreateFolderCommandHandler(IFolderRepository folders, ICurrentUser currentUser)
    : ICommandHandler<CreateFolderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateFolderCommand request, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.GetRequiredUserId();
        if (request.ParentId is { } parentId && !await folders.ExistsOwnedAsync(parentId, ownerId, cancellationToken))
        {
            throw new FolderException.NotFound(parentId);
        }

        var folder = Folder.Create(Guid.CreateVersion7(), ownerId, request.Name, request.Description, request.ParentId);
        folders.Add(folder);

        return folder.Id;
    }
}
