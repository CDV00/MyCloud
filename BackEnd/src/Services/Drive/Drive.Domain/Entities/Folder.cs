using Drive.Domain.Events;
using Drive.Domain.Exceptions;
using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Entities;

/// <summary>
/// Thư mục. Mô hình chuyển tiếp — Phase 2 gộp Folder/File thành aggregate DriveItem (ADR-005).
/// </summary>
public sealed class Folder : AggregateRoot<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 255;
    public const int DescriptionMaxLength = 1000;

    private Folder()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    /// <summary>Chủ sở hữu. Phase 1–3 chỉ chủ sở hữu được truy cập; Phase 4 thêm quyền chia sẻ (Sharing service).</summary>
    public Guid OwnerId { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? ModifiedOnUtc { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static Folder Create(Guid id, Guid ownerId, string name, string? description, Guid? parentId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A folder must have an owner.", nameof(ownerId));
        }

        var folder = new Folder
        {
            Id = id,
            OwnerId = ownerId,
            Name = ValidateName(name),
            Description = description?.Trim() ?? string.Empty,
            ParentId = parentId,
        };

        folder.RaiseDomainEvent(new FolderCreatedDomainEvent(folder.Id));
        return folder;
    }

    public void Update(string name, string? description, Guid? parentId)
    {
        if (parentId == Id)
        {
            throw new FolderException.InvalidParent(Id);
        }

        Name = ValidateName(name);
        Description = description?.Trim() ?? string.Empty;
        ParentId = parentId;

        RaiseDomainEvent(new FolderUpdatedDomainEvent(Id));
    }

    public void Delete() => RaiseDomainEvent(new FolderDeletedDomainEvent(Id));

    internal static string ValidateName(string name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength || trimmed.IndexOfAny(['/', '\\']) >= 0 || trimmed.Any(char.IsControl))
        {
            throw new FolderException.InvalidName(name ?? string.Empty);
        }

        return trimmed;
    }
}
