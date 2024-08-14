namespace MyDrive.Query.Domain.Exceptions;

public static class FolderException
{
    public class FolderNotFoundException : NotFoundException
    {
        public FolderNotFoundException(Guid folderId)
            : base($"The folder with the id {folderId} was not found.") { }
    }

    public class FolderFieldException : NotFoundException
    {
        public FolderFieldException(string folderField)
            : base($"The folder with the field {folderField} is not correct.") { }
    }
}
