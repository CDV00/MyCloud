namespace MyDrive.BuidingBlock.Contract.Services.V1.Folder;

public static class Response
{
    public record FolderResponse(Guid Id, string Name, string Description, Guid? ParentId);
}