using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Items;

/// <summary>Thay đổi đường dẫn của cả cây con khi di chuyển một thư mục.</summary>
public sealed record SubtreeMove(Guid ItemId, string OldPath, string NewPath, int DepthDelta);

/// <summary>Đánh dấu mọi con cháu chưa bị xoá của <paramref name="ItemId"/> là thuộc nhóm thùng rác của nó.</summary>
public sealed record SubtreeTrash(Guid ItemId, string Path, DateTimeOffset TrashedAt, Guid By);

/// <summary>Kết quả khôi phục: các con cháu cùng nhóm được khôi phục theo; <see cref="Move"/> khác null nếu đổi chỗ.</summary>
public sealed record SubtreeRestore(Guid TrashRootId, SubtreeMove? Move);

/// <summary>
/// File hoặc thư mục (ADR-005). Cây lưu bằng <see cref="ParentId"/> (adjacency) + <see cref="Path"/> (materialized:
/// <c>/{rootId}/{id1}/.../{id}/</c>) để lấy tổ tiên, kiểm tra "là con cháu" và di chuyển cả cây bằng một câu UPDATE.
/// Mọi thay đổi tăng <see cref="Revision"/> — dùng cho <c>If-Match</c> và để consumer bỏ qua event cũ.
/// <para>
/// Thùng rác (docs/05 §6): xoá một thư mục đánh dấu nó và mọi con cháu <b>chưa bị xoá</b> với cùng
/// <see cref="TrashRootId"/> = id của nó (một "nhóm"). Khôi phục trả lại đúng cả nhóm; item con đã bị xoá riêng từ trước
/// vẫn nằm trong thùng rác như một nhóm khác.
/// </para>
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

    /// <summary>Khoá so trùng tên (NFC + chữ thường) — unique trong một thư mục, chỉ xét item chưa bị xoá.</summary>
    public string NameKey { get; private set; } = string.Empty;

    public string? MimeType { get; private set; }

    public long SizeBytes { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    /// <summary>Số của version mới nhất từng tạo (version cũ có thể đã bị xoá nên không dùng COUNT).</summary>
    public int LastVersionNo { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    public bool IsRoot { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset? TrashedAt { get; private set; }

    /// <summary>Item gốc của nhóm bị xoá cùng lúc (chính nó nếu người dùng xoá item này).</summary>
    public Guid? TrashRootId { get; private set; }

    public Guid? TrashedBy { get; private set; }

    /// <summary>
    /// Version đã có thumbnail (Media Worker, ADR-015). Chỉ có nghĩa khi bằng <see cref="CurrentVersionId"/>: nội dung
    /// mới ⇒ thumbnail cũ không còn đúng cho tới khi Media tạo xong thumbnail của version mới.
    /// </summary>
    public Guid? ThumbnailVersionId { get; private set; }

    public bool HasThumbnail => ThumbnailVersionId is { } version && version == CurrentVersionId;

    public DateTimeOffset CreatedOnUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? ModifiedOnUtc { get; set; }

    public Guid? ModifiedBy { get; set; }

    /// <summary>Chỉ chứa version được thêm trong phiên làm việc hiện tại (không tự nạp lịch sử).</summary>
    public IReadOnlyCollection<FileVersion> Versions => _versions;

    public bool IsFolder => Kind == ItemKind.Folder;

    public bool IsTrashed => TrashedAt is not null;

    /// <summary>Item người dùng đã xoá (hiện trong thùng rác), không phải item nằm trong một thư mục đã xoá.</summary>
    public bool IsTrashRoot => TrashRootId == Id;

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
    public static DriveItem CreateFile(
        Guid id, DriveItem parent, ItemName name, Guid versionId, Guid blobId, long sizeBytes, string mimeType,
        VersionSource source = VersionSource.Upload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        DriveItem file = NewChild(id, parent, ItemKind.File, name);
        file.RaiseDomainEvent(new ItemCreatedDomainEvent(id));
        file.AddVersion(versionId, blobId, sizeBytes, mimeType, source);
        return file;
    }

    public void Rename(ItemName name, long? expectedRevision)
    {
        EnsureRevision(expectedRevision);
        EnsureNotRoot();
        EnsureNotTrashed();

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
        EnsureNotRoot();
        EnsureNotTrashed();
        EnsureCanContain(newParent);
        if (newParent.Id == ParentId)
        {
            return null;
        }

        Guid oldParentId = ParentId!.Value;
        SubtreeMove move = Relocate(newParent);
        Revision++;
        RaiseDomainEvent(new ItemMovedDomainEvent(Id, oldParentId));
        return move;
    }

    /// <summary>Chuyển vào thùng rác. Đã ở thùng rác ⇒ không làm gì (gọi lặp vô hại); trả về phần cây con cần đánh dấu.</summary>
    public SubtreeTrash? Trash(Guid by, DateTimeOffset now, long? expectedRevision)
    {
        EnsureRevision(expectedRevision);
        EnsureNotRoot();
        if (IsTrashed)
        {
            return null;
        }

        TrashedAt = now;
        TrashRootId = Id;
        TrashedBy = by;
        Revision++;
        RaiseDomainEvent(new ItemTrashedDomainEvent(Id));
        return IsFolder ? new SubtreeTrash(Id, Path, now, by) : null;
    }

    /// <summary>
    /// Khôi phục nhóm do chính item này đứng đầu vào <paramref name="target"/> (thư mục cha cũ, hoặc "Drive của tôi" nếu
    /// cha cũ đang nằm trong thùng rác), với <paramref name="name"/> đã được chọn để không trùng trong thư mục đích.
    /// </summary>
    public SubtreeRestore Restore(DriveItem target, ItemName name)
    {
        if (!IsTrashed)
        {
            throw new DriveException.NotInTrash(Id);
        }

        if (!IsTrashRoot)
        {
            throw new DriveException.RestoreContainingFolder(Id, TrashRootId!.Value);
        }

        if (target.IsTrashed)
        {
            throw new InvalidOperationException("Cannot restore into a folder that is in the trash.");
        }

        EnsureCanContain(target);
        SubtreeMove? move = target.Id == ParentId ? null : Relocate(target);
        Name = name.Value;
        NameKey = name.Key;
        TrashedAt = null;
        TrashRootId = null;
        TrashedBy = null;
        Revision++;
        RaiseDomainEvent(new ItemRestoredDomainEvent(Id));
        return new SubtreeRestore(Id, IsFolder ? move : null);
    }

    /// <summary>Thêm nội dung mới và đặt làm version hiện tại. Số version tăng dần, không dùng lại số đã xoá.</summary>
    public FileVersion AddVersion(
        Guid versionId, Guid blobId, long sizeBytes, string mimeType, VersionSource source = VersionSource.Upload, int? restoredFrom = null,
        string? note = null)
    {
        if (Kind != ItemKind.File)
        {
            throw new DriveException.NotAFile(Id);
        }

        EnsureNotTrashed();

        var version = FileVersion.Create(versionId, Id, LastVersionNo + 1, blobId, sizeBytes, mimeType, source, restoredFrom, note);
        _versions.Add(version);
        LastVersionNo = version.VersionNo;
        CurrentVersionId = versionId;
        SizeBytes = sizeBytes;
        MimeType = version.MimeType;
        if (version.VersionNo > 1)
        {
            Revision++;
        }

        RaiseDomainEvent(new FileVersionAddedDomainEvent(Id, versionId));
        return version;
    }

    /// <summary>Xoá các version cũ (không được là version hiện tại). Storage xoá blob và trả quota khi nhận event.</summary>
    public void DeleteVersions(IReadOnlyCollection<FileVersion> versions)
    {
        if (versions.Count == 0)
        {
            return;
        }

        EnsureNotTrashed();
        if (versions.Any(v => v.ItemId != Id))
        {
            throw new ArgumentException("Every version must belong to this file.", nameof(versions));
        }

        if (versions.Any(v => v.Id == CurrentVersionId))
        {
            throw new DriveException.CannotDeleteCurrentVersion(Id);
        }

        Revision++;
        RaiseDomainEvent(new FileVersionsDeletedDomainEvent(
            Id, versions.Select(v => v.Id).ToArray(), versions.Select(v => v.BlobId).ToArray()));
    }

    /// <summary>
    /// Version cũ vượt quá <paramref name="maxVersions"/> (tính cả version hiện tại), cũ nhất trước. Version hiện tại không
    /// bao giờ bị chọn.
    /// </summary>
    public IReadOnlyList<FileVersion> VersionsBeyondLimit(IEnumerable<FileVersion> history, int maxVersions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVersions, 1);
        FileVersion[] older = history.Where(v => v.Id != CurrentVersionId).OrderByDescending(v => v.VersionNo).ToArray();
        return older.Skip(maxVersions - 1).Reverse().ToArray();
    }

    public static IEnumerable<Guid> ParsePath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(segment => Guid.ParseExact(segment, "N"));

    private static DriveItem NewChild(Guid id, DriveItem parent, ItemKind kind, ItemName name)
    {
        EnsureCanContain(parent);
        if (parent.IsTrashed)
        {
            throw new DriveException.ItemTrashed(parent.Id);
        }

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

    private SubtreeMove Relocate(DriveItem newParent)
    {
        // Path của đích bắt đầu bằng path của mình ⇒ đích là chính nó hoặc con cháu ⇒ tạo vòng.
        if (newParent.Path.StartsWith(Path, StringComparison.Ordinal))
        {
            throw new DriveException.InvalidMove("A folder cannot be moved into itself or one of its subfolders.");
        }

        if (newParent.IsTrashed)
        {
            throw new DriveException.ItemTrashed(newParent.Id);
        }

        // Mọi item trong một cây thuộc chủ sở hữu của cây (người đó trả dung lượng) ⇒ người được chia sẻ không kéo
        // item của người khác vào Drive của mình (hay ngược lại) — sao chép thay vì di chuyển.
        if (newParent.OwnerId != OwnerId)
        {
            throw new DriveException.InvalidMove("Items can only be moved between folders of the same owner.");
        }

        string oldPath = Path;
        int oldDepth = Depth;
        ParentId = newParent.Id;
        Path = $"{newParent.Path}{Id:N}/";
        Depth = newParent.Depth + 1;
        return new SubtreeMove(Id, oldPath, Path, Depth - oldDepth);
    }

    private static void EnsureCanContain(DriveItem parent)
    {
        if (!parent.IsFolder)
        {
            throw new DriveException.NotAFolder(parent.Id);
        }
    }

    private void EnsureNotRoot()
    {
        if (IsRoot)
        {
            throw new DriveException.RootImmutable();
        }
    }

    private void EnsureNotTrashed()
    {
        if (IsTrashed)
        {
            throw new DriveException.ItemTrashed(Id);
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
