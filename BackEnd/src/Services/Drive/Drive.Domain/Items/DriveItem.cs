using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Items;

/// <summary>Thay đổi đường dẫn của cả cây con khi di chuyển một thư mục.</summary>
public sealed record SubtreeMove(Guid ItemId, string OldPath, string NewPath, int DepthDelta);

/// <summary>
/// File hoặc thư mục (ADR-005). Cây lưu bằng <see cref="ParentId"/> (adjacency) + <see cref="Path"/> (materialized:
/// <c>/{rootId}/{id1}/.../{id}/</c>) để lấy tổ tiên, kiểm tra "là con cháu" và di chuyển cả cây bằng một câu UPDATE.
/// Mọi thay đổi tăng <see cref="Revision"/> — dùng cho <c>If-Match</c> và để consumer bỏ qua event cũ.
/// </summary>
public sealed class DriveItem : AggregateRoot<Guid>, IAuditableEntity
{
    public const string RootName = "My Drive";

    private readonly List<FileVersion> _versions = [];

    private DriveItem()
    {
    }

    public Guid OwnerId { get; private set; }

    /// <summary>Null chỉ với thư mục gốc của mỗi người dùng.</summary>
    public Guid? ParentId { get; private set; }

    public ItemKind Kind { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Khoá so trùng tên (NFC + chữ thường) — unique trong một thư mục.</summary>
    public string NameKey { get; private set; } = string.Empty;

    public string? MimeType { get; private set; }

    public long SizeBytes { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    public bool IsRoot { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? ModifiedOnUtc { get; set; }

    public Guid? ModifiedBy { get; set; }

    /// <summary>Chỉ chứa version được thêm trong phiên làm việc hiện tại (không tự nạp lịch sử).</summary>
    public IReadOnlyCollection<FileVersion> Versions => _versions;

    public bool IsFolder => Kind == ItemKind.Folder;

    /// <summary>Tổ tiên từ gốc tới cha trực tiếp (đọc từ <see cref="Path"/>, không truy vấn DB).</summary>
    public IReadOnlyList<Guid> AncestorIds => ParsePath(Path).SkipLast(1).ToArray();

    public static DriveItem CreateRoot(Guid id, Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An item must have an owner.", nameof(ownerId));
        }

        var root = new DriveItem
        {
            Id = id,
            OwnerId = ownerId,
            Kind = ItemKind.Folder,
            Name = RootName,
            NameKey = ItemName.KeyOf(RootName),
            Path = $"/{id:N}/",
            Depth = 0,
            IsRoot = true,
            Revision = 1,
        };
        root.RaiseDomainEvent(new ItemCreatedDomainEvent(id));
        return root;
    }

    public static DriveItem CreateFolder(Guid id, DriveItem parent, ItemName name)
    {
        DriveItem folder = NewChild(id, parent, ItemKind.Folder, name);
        folder.RaiseDomainEvent(new ItemCreatedDomainEvent(id));
        return folder;
    }

    /// <summary>File mới cùng version đầu tiên (nội dung đã nằm trong blob của Storage).</summary>
    public static DriveItem CreateFile(Guid id, DriveItem parent, ItemName name, Guid versionId, Guid blobId, long sizeBytes, string mimeType)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        DriveItem file = NewChild(id, parent, ItemKind.File, name);
        file.RaiseDomainEvent(new ItemCreatedDomainEvent(id));
        file.AddVersion(versionId, versionNo: 1, blobId, sizeBytes, mimeType);
        return file;
    }

    public void Rename(ItemName name, long? expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (IsRoot)
        {
            throw new DriveException.RootImmutable();
        }

        if (name.Value == Name)
        {
            return;
        }

        Name = name.Value;
        NameKey = name.Key;
        Revision++;
        RaiseDomainEvent(new ItemRenamedDomainEvent(Id));
    }

    /// <summary>
    /// Chuyển sang thư mục khác. Trả về thay đổi đường dẫn mà cây con (nếu là thư mục) phải áp dụng; null nếu không đổi.
    /// </summary>
    public SubtreeMove? MoveTo(DriveItem newParent, long? expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (IsRoot)
        {
            throw new DriveException.RootImmutable();
        }

        EnsureCanContain(newParent);
        if (newParent.Id == ParentId)
        {
            return null;
        }

        // Path của đích bắt đầu bằng path của mình ⇒ đích là chính nó hoặc con cháu ⇒ tạo vòng.
        if (newParent.Path.StartsWith(Path, StringComparison.Ordinal))
        {
            throw new DriveException.InvalidMove("A folder cannot be moved into itself or one of its subfolders.");
        }

        Guid oldParentId = ParentId!.Value;
        string oldPath = Path;
        int oldDepth = Depth;

        ParentId = newParent.Id;
        Path = $"{newParent.Path}{Id:N}/";
        Depth = newParent.Depth + 1;
        Revision++;
        RaiseDomainEvent(new ItemMovedDomainEvent(Id, oldParentId));

        return new SubtreeMove(Id, oldPath, Path, Depth - oldDepth);
    }

    public FileVersion AddVersion(Guid versionId, int versionNo, Guid blobId, long sizeBytes, string mimeType)
    {
        if (Kind != ItemKind.File)
        {
            throw new DriveException.NotAFile(Id);
        }

        var version = FileVersion.Create(versionId, Id, versionNo, blobId, sizeBytes, mimeType);
        _versions.Add(version);
        CurrentVersionId = versionId;
        SizeBytes = sizeBytes;
        MimeType = version.MimeType;
        if (versionNo > 1)
        {
            Revision++;
        }

        RaiseDomainEvent(new FileVersionAddedDomainEvent(Id, versionId));
        return version;
    }

    public static IEnumerable<Guid> ParsePath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(segment => Guid.ParseExact(segment, "N"));

    private static DriveItem NewChild(Guid id, DriveItem parent, ItemKind kind, ItemName name)
    {
        EnsureCanContain(parent);
        return new DriveItem
        {
            Id = id,
            OwnerId = parent.OwnerId, // item luôn thuộc chủ sở hữu của thư mục chứa nó
            ParentId = parent.Id,
            Kind = kind,
            Name = name.Value,
            NameKey = name.Key,
            Path = $"{parent.Path}{id:N}/",
            Depth = parent.Depth + 1,
            Revision = 1,
        };
    }

    private static void EnsureCanContain(DriveItem parent)
    {
        if (!parent.IsFolder)
        {
            throw new DriveException.NotAFolder(parent.Id);
        }
    }

    private void EnsureRevision(long? expectedRevision)
    {
        if (expectedRevision is { } expected && expected != Revision)
        {
            throw new DriveException.RevisionMismatch(expected, Revision);
        }
    }
}
