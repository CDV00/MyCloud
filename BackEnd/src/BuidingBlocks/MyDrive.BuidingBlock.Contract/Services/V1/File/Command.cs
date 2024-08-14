using Microsoft.AspNetCore.Http;
using MyDrive.BuidingBlock.Contract.Abstractions.Messages;

namespace MyDrive.BuidingBlock.Contract.Services.V1.File;

public static class Command
{

    public record CreateFile2Command(Guid FolderId, IFormFile file) : ICommand;
    public record CreateFileCommand(string FileString ,string Name, string type, decimal size, string description, Guid FolderId) : ICommand;

    public record UpdateFileCommand(Guid Id, string Name, string type, decimal size, string description, Guid FolderId) : ICommand;

    public record DeleteFileCommand(Guid Id) : ICommand;
}