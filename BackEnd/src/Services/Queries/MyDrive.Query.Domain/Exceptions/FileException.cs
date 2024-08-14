namespace MyDrive.Query.Domain.Exceptions;

public static class FileException
{
    public class FileNotFoundException : NotFoundException
    {
        public FileNotFoundException(Guid fileId)
            : base($"The file with the id {fileId} was not found.") { }
    }

    public class FileFieldException : NotFoundException
    {
        public FileFieldException(string fileField)
            : base($"The file with the field {fileField} is not correct.") { }
    }
}
