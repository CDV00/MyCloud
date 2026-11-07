using MyCloud.BuildingBlocks.Domain.Exceptions;

namespace Drive.Domain.Exceptions;

public static class FolderException
{
    public sealed class NotFound(Guid folderId)
        : NotFoundException($"The folder with the id {folderId} was not found.");

    public sealed class InvalidName(string name)
        : BadRequestException($"The folder name '{name}' is invalid. It must be 1-255 characters and must not contain '/' or '\\'.");

    public sealed class InvalidParent(Guid folderId)
        : BadRequestException($"The folder {folderId} cannot be its own parent.");

    public sealed class NotEmpty(Guid folderId)
        : ConflictException($"The folder {folderId} is not empty.");
}

public static class FileException
{
    public sealed class NotFound(Guid fileId)
        : NotFoundException($"The file with the id {fileId} was not found.");

    public sealed class InvalidName(string name)
        : BadRequestException($"The file name '{name}' is invalid. It must be 1-255 characters and must not contain '/' or '\\'.");

    public sealed class InvalidSize(long size)
        : BadRequestException($"The file size {size} is invalid.");

    public sealed class MissingContent(Guid fileId)
        : BadRequestException($"The file {fileId} has no stored content.");
}
