using MyCloud.BuildingBlocks.Domain.Exceptions;

namespace Drive.Domain.Items;

/// <summary>Lỗi nghiệp vụ của Drive; <c>Code</c> ổn định để client hiển thị thông báo riêng.</summary>
public static class DriveException
{
    /// <summary>Item không tồn tại HOẶC người gọi không có quyền — luôn 404 để không lộ id hợp lệ.</summary>
    public sealed class ItemNotFound(Guid itemId) : NotFoundException($"Item '{itemId}' was not found.")
    {
        public override string Code => "DRIVE.ITEM_NOT_FOUND";
    }

    public sealed class UploadNotFound(Guid uploadId) : NotFoundException($"Upload '{uploadId}' was not found.")
    {
        public override string Code => "DRIVE.UPLOAD_NOT_FOUND";
    }

    public sealed class InvalidName(string name)
        : BadRequestException($"The name '{name}' is invalid. It must be 1-255 characters and must not contain '/', '\\' or control characters.")
    {
        public override string Code => "DRIVE.INVALID_NAME";
    }

    public sealed class NameConflict(string name) : ConflictException(name.Length == 0
        ? "An item with this name already exists in this folder."
        : $"An item named '{name}' already exists in this folder.")
    {
        public override string Code => "DRIVE.NAME_CONFLICT";
    }

    public sealed class NotAFolder(Guid itemId) : BadRequestException($"Item '{itemId}' is not a folder.")
    {
        public override string Code => "DRIVE.NOT_A_FOLDER";
    }

    public sealed class NotAFile(Guid itemId) : BadRequestException($"Item '{itemId}' is not a file.")
    {
        public override string Code => "DRIVE.NOT_A_FILE";
    }

    public sealed class InvalidMove(string reason) : ConflictException(reason)
    {
        public override string Code => "DRIVE.INVALID_MOVE";
    }

    public sealed class RootImmutable() : BadRequestException("This operation is not allowed on the root folder.")
    {
        public override string Code => "DRIVE.ROOT_IMMUTABLE";
    }

    /// <summary>Client sửa dựa trên bản cũ (<c>If-Match</c> không khớp revision hiện tại).</summary>
    public sealed class RevisionMismatch(long expected, long actual)
        : PreconditionFailedException($"The item has changed (revision {actual}, expected {expected}). Reload and try again.")
    {
        public override string Code => "DRIVE.REVISION_MISMATCH";
    }

    /// <summary>Thư mục gốc vừa được tạo bởi một yêu cầu khác cùng lúc (vd consumer UserRegistered) — đọc lại là có.</summary>
    public sealed class RootCreatedConcurrently() : ConflictException("The root folder was created concurrently.");

    /// <summary>Hai người cùng sửa một item; bản lưu sau bị từ chối thay vì ghi đè âm thầm.</summary>
    public sealed class ConcurrentModification()
        : PreconditionFailedException("The item was changed by someone else. Reload and try again.")
    {
        public override string Code => "DRIVE.REVISION_MISMATCH";
    }

    /// <summary>Thấy item nhưng vai trò không đủ cho thao tác (vd người xem muốn đổi tên).</summary>
    public sealed class AccessDenied(Guid itemId) : ForbiddenException($"You do not have permission to do this on item '{itemId}'.")
    {
        public override string Code => "DRIVE.ACCESS_DENIED";
    }

    public sealed class UserNotFound() : NotFoundException("No MyCloud account uses this email address.")
    {
        public override string Code => "DRIVE.USER_NOT_FOUND";
    }

    public sealed class CannotShareWithOwner() : ConflictException("The owner already has full access to this item.")
    {
        public override string Code => "DRIVE.ALREADY_OWNER";
    }

    public sealed class LinkNotFound() : NotFoundException("This link does not exist, has expired or was turned off.")
    {
        public override string Code => "DRIVE.LINK_NOT_FOUND";
    }

    /// <summary>Link có mật khẩu: thiếu hoặc sai.</summary>
    public sealed class LinkPasswordRequired() : ForbiddenException("This link is protected by a password.")
    {
        public override string Code => "DRIVE.LINK_PASSWORD_REQUIRED";
    }

    /// <summary>Sửa / thêm vào item đang ở thùng rác — phải khôi phục trước.</summary>
    public sealed class ItemTrashed(Guid itemId) : ConflictException($"Item '{itemId}' is in the trash. Restore it first.")
    {
        public override string Code => "DRIVE.ITEM_TRASHED";
    }

    public sealed class NotInTrash(Guid itemId) : ConflictException($"Item '{itemId}' is not in the trash.")
    {
        public override string Code => "DRIVE.NOT_IN_TRASH";
    }

    /// <summary>Item nằm trong một thư mục đã bị xoá: khôi phục thư mục đó (cả nhóm), không tách lẻ.</summary>
    public sealed class RestoreContainingFolder(Guid itemId, Guid trashRootId)
        : ConflictException($"Item '{itemId}' was deleted together with folder '{trashRootId}'. Restore that folder instead.")
    {
        public override string Code => "DRIVE.RESTORE_CONTAINING_FOLDER";
    }

    public sealed class VersionNotFound(Guid versionId) : NotFoundException($"Version '{versionId}' was not found.")
    {
        public override string Code => "DRIVE.VERSION_NOT_FOUND";
    }

    public sealed class CannotDeleteCurrentVersion(Guid itemId)
        : ConflictException($"The current version of item '{itemId}' cannot be deleted.")
    {
        public override string Code => "DRIVE.CANNOT_DELETE_CURRENT_VERSION";
    }

    /// <summary>Sao chép thư mục quá lớn để làm trong một yêu cầu (job nền cần định danh service — Phase 4/5).</summary>
    public sealed class CopyTooLarge(int maxItems)
        : LimitExceededException($"A folder with more than {maxItems} items cannot be copied at once.")
    {
        public override string Code => "DRIVE.COPY_TOO_LARGE";
    }

    /// <summary>Kiểm tra trước khi sao chép: tổng dung lượng các file vượt dung lượng còn trống.</summary>
    public sealed class ArchiveTooLarge(string reason) : LimitExceededException(reason)
    {
        public override string Code => "DRIVE.ARCHIVE_TOO_LARGE";
    }

    public sealed class TooManyFolders(int max) : LimitExceededException($"At most {max} folders can be created in one request.")
    {
        public override string Code => "DRIVE.TOO_MANY_FOLDERS";
    }

    public sealed class QuotaExceeded() : LimitExceededException("Not enough storage space to copy these files.")
    {
        public override string Code => "STORAGE.QUOTA_EXCEEDED";
    }
}
