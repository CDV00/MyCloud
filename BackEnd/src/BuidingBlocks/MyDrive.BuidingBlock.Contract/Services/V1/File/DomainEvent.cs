using MyDrive.BuidingBlock.Contract.Abstractions.Messages;

namespace MyDrive.BuidingBlock.Contract.Services.V1.File;

public static class DomainEvent
{
    public record FileCreated(Guid IdEvent, Guid Id, string Name, string Type, decimal Size, string Description, string StoredName, Guid FolderId) : IDomainEvent, ICommand;
    public record FileDeleted(Guid IdEvent, Guid Id) : IDomainEvent, ICommand;
    public record FileUpdated(Guid IdEvent, Guid Id, string Name, string type, decimal size, string Description, string StoredName, Guid FolderId) : IDomainEvent, ICommand;
}
