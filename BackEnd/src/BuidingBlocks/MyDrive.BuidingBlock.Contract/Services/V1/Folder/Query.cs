using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using static MyDrive.BuidingBlock.Contract.Services.V1.Folder.Response;

namespace MyDrive.BuidingBlock.Contract.Services.V1.Folder;

public static class Query
{
    public record GetFoldersQuery() : IQuery<List<FolderResponse>>;
    public record GetFolderByIdQuery(Guid Id) : IQuery<FolderResponse>;
}
