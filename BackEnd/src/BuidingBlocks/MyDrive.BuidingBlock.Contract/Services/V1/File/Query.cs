using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using static MyDrive.BuidingBlock.Contract.Services.V1.File.Response;
using static MyDrive.BuidingBlock.Contract.Services.V1.Folder.Response;

namespace MyDrive.BuidingBlock.Contract.Services.V1.File;

public static class Query
{
    public record GetFilesQuery() : IQuery<List<FileResponse>>;
    public record GetFileByIdQuery(Guid Id) : IQuery<FileResponse>;
}
