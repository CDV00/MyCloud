using MyDrive.BuidingBlock.Contract.Abstractions.Messages;

namespace MyDrive.BuidingBlock.Contract.Services.V1.Folder;

public static class Command
{
    public record CreateFolderCommand(string Name, string Description, Guid? parentId) : ICommand;

    public record UpdateFolderCommand(Guid Id, string Name, string Description, Guid? parentId) : ICommand;

    public record DeleteFolderCommand(Guid Id) : ICommand;
}
