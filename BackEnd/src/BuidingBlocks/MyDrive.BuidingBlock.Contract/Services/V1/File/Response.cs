namespace MyDrive.BuidingBlock.Contract.Services.V1.File;

public static class Response
{
    public record FileResponse(Guid Id, string Name, string Type, decimal Size, string Description, Guid FolderId);
}