using Drive.Domain.Events;
using Drive.Domain.Exceptions;
using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Entities;

/// <summary>
/// File. Mô hình chuyển tiếp — Phase 2 thay bằng DriveItem + FileVersion, nội dung nằm ở Storage service.
/// </summary>
public sealed class File : AggregateRoot<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 255;
    public const int DescriptionMaxLength = 1000;

    private File()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Khoá của nội dung trong kho lưu trữ (không phải tên file của người dùng).</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public Guid FolderId { get; private set; }

    /// <summary>Chủ sở hữu — luôn trùng chủ sở hữu của thư mục chứa file.</summary>
    public Guid OwnerId { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? ModifiedOnUtc { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static File Create(
        Guid id,
        Guid ownerId,
        string name,
        string storageKey,
        string contentType,
        long sizeBytes,
        string? description,
        Guid folderId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A file must have an owner.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new FileException.MissingContent(id);
        }

        if (sizeBytes < 0)
        {
            throw new FileException.InvalidSize(sizeBytes);
        }

        var file = new File
        {
            Id = id,
            OwnerId = ownerId,
            Name = ValidateName(name),
            StorageKey = storageKey,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeBytes = sizeBytes,
            Description = description?.Trim() ?? string.Empty,
            FolderId = folderId,
        };

        file.RaiseDomainEvent(new FileCreatedDomainEvent(file.Id));
        return file;
    }

    /// <summary>Đổi metadata. Nội dung (StorageKey, kích thước) không đổi ở đây — sửa lỗi S6 của bản cũ.</summary>
    public void UpdateMetadata(string name, string? description, Guid folderId)
    {
        Name = ValidateName(name);
        Description = description?.Trim() ?? string.Empty;
        FolderId = folderId;

        RaiseDomainEvent(new FileUpdatedDomainEvent(Id));
    }

    public void Delete() => RaiseDomainEvent(new FileDeletedDomainEvent(Id, StorageKey));

    private static string ValidateName(string name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength || trimmed.IndexOfAny(['/', '\\']) >= 0 || trimmed.Any(char.IsControl))
        {
            throw new FileException.InvalidName(name ?? string.Empty);
        }

        return trimmed;
    }
}
