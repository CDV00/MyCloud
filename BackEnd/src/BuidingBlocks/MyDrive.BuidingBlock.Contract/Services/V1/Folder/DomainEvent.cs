using MyDrive.BuidingBlock.Contract.Abstractions.Messages;

namespace MyDrive.BuidingBlock.Contract.Services.V1.Folder;

public static class DomainEvent
{
    public record FolderCreated(Guid IdEvent, Guid Id, string Name, string Description, Guid? parentId) : IDomainEvent, ICommand;
    public record FolderDeleted(Guid IdEvent, Guid Id) : IDomainEvent, ICommand;
    public record FolderUpdated(Guid IdEvent, Guid Id, string Name, string Description, Guid? parentId) : IDomainEvent, ICommand;
}
