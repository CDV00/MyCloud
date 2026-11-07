using MyCloud.BuildingBlocks.Domain.Exceptions;

namespace Storage.Api.Uploads;

public sealed class QuotaExceededException()
    : LimitExceededException("Not enough storage space for this file.")
{
    public override string Code => "STORAGE.QUOTA_EXCEEDED";
}

public sealed class FileTooLargeException(long maxBytes)
    : LimitExceededException($"The file exceeds the maximum size of {maxBytes} bytes.")
{
    public override string Code => "STORAGE.FILE_TOO_LARGE";
}

public sealed class UploadNotFoundException(Guid sessionId)
    : NotFoundException($"Upload session '{sessionId}' was not found.")
{
    public override string Code => "STORAGE.UPLOAD_NOT_FOUND";
}

public sealed class BlobNotFoundException(Guid blobId)
    : NotFoundException($"Blob '{blobId}' was not found.")
{
    public override string Code => "STORAGE.BLOB_NOT_FOUND";
}

public sealed class UploadNotPendingException(Guid sessionId)
    : ConflictException($"Upload session '{sessionId}' is no longer in progress.")
{
    public override string Code => "STORAGE.UPLOAD_NOT_PENDING";
}

public sealed class InvalidUploadException(string message) : BadRequestException(message)
{
    public override string Code => "STORAGE.INVALID_UPLOAD";
}

/// <summary>Byte thật khác số đã khai báo (và đã giữ chỗ quota) — từ chối để không ai lách được quota.</summary>
public sealed class SizeMismatchException(long expected, long actual)
    : BadRequestException($"Uploaded size {actual} does not match the declared size {expected}.")
{
    public override string Code => "STORAGE.SIZE_MISMATCH";
}
