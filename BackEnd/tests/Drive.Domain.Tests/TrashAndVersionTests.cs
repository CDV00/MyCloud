using Drive.Domain.Items;

namespace Drive.Domain.Tests;

public sealed class TrashAndVersionTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static DriveItem Root() => DriveItem.CreateRoot(Guid.NewGuid(), Owner);

    private static DriveItem Folder(DriveItem parent, string name = "A") =>
        DriveItem.CreateFolder(Guid.NewGuid(), parent, ItemName.Create(name));

    private static DriveItem File(DriveItem parent, string name = "a.txt") =>
        DriveItem.CreateFile(Guid.NewGuid(), parent, ItemName.Create(name), Guid.NewGuid(), Guid.NewGuid(), 42, "text/plain");

    [Fact]
    public void Trash_Folder_MarksItselfAsGroupRoot_AndAsksToTrashSubtree()
    {
        DriveItem folder = Folder(Root());
        folder.ClearDomainEvents();

        SubtreeTrash? subtree = folder.Trash(Owner, Now, expectedRevision: 1);

        folder.IsTrashed.ShouldBeTrue();
        folder.IsTrashRoot.ShouldBeTrue();
        folder.TrashedAt.ShouldBe(Now);
        folder.Revision.ShouldBe(2);
        subtree.ShouldBe(new SubtreeTrash(folder.Id, folder.Path, Now, Owner));
        folder.GetDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<ItemTrashedDomainEvent>();
    }

    [Fact]
    public void Trash_File_HasNoSubtree_AndTrashingAgainIsNoOp()
    {
        DriveItem file = File(Root());

        file.Trash(Owner, Now, null).ShouldBeNull();
        long revision = file.Revision;
        file.Trash(Owner, Now.AddHours(1), null).ShouldBeNull();

        file.Revision.ShouldBe(revision);
        file.TrashedAt.ShouldBe(Now);
    }

    [Fact]
    public void Trash_Root_IsRejected()
    {
        Should.Throw<DriveException.RootImmutable>(() => Root().Trash(Owner, Now, null));
    }

    [Fact]
    public void TrashedItem_CannotBeRenamedMovedOrReceiveChildren()
    {
        DriveItem root = Root();
        DriveItem folder = Folder(root);
        DriveItem other = Folder(root, "B");
        folder.Trash(Owner, Now, null);

        Should.Throw<DriveException.ItemTrashed>(() => folder.Rename(ItemName.Create("x"), null));
        Should.Throw<DriveException.ItemTrashed>(() => folder.MoveTo(other, null));
        Should.Throw<DriveException.ItemTrashed>(() => Folder(folder, "child"));
        Should.Throw<DriveException.ItemTrashed>(() => other.MoveTo(folder, null)); // không chuyển vào thư mục đã xoá
    }

    [Fact]
    public void Restore_ToSameParent_KeepsPath_AndRaisesRestored()
    {
        DriveItem root = Root();
        DriveItem folder = Folder(root);
        folder.Trash(Owner, Now, null);
        folder.ClearDomainEvents();

        SubtreeRestore restore = folder.Restore(root, ItemName.Create("A"));

        folder.IsTrashed.ShouldBeFalse();
        folder.TrashRootId.ShouldBeNull();
        restore.ShouldBe(new SubtreeRestore(folder.Id, null));
        folder.GetDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<ItemRestoredDomainEvent>();
    }

    [Fact]
    public void Restore_ToAnotherFolder_RelocatesAndRenames()
    {
        DriveItem root = Root();
        DriveItem parent = Folder(root, "P");
        DriveItem folder = Folder(parent, "A");
        string oldPath = folder.Path;
        folder.Trash(Owner, Now, null);

        SubtreeRestore restore = folder.Restore(root, ItemName.Create("A (1)"));

        folder.ParentId.ShouldBe(root.Id);
        folder.Name.ShouldBe("A (1)");
        folder.Path.ShouldBe($"{root.Path}{folder.Id:N}/");
        restore.Move.ShouldBe(new SubtreeMove(folder.Id, oldPath, folder.Path, -1));
    }

    [Fact]
    public void Restore_ItemDeletedTogetherWithItsFolder_MustRestoreTheFolder()
    {
        DriveItem root = Root();
        DriveItem folder = Folder(root);
        DriveItem child = File(folder);
        folder.Trash(Owner, Now, null);
        // Mô phỏng câu UPDATE cây con: con thuộc nhóm của thư mục.
        typeof(DriveItem).GetProperty(nameof(DriveItem.TrashedAt))!.SetValue(child, Now);
        typeof(DriveItem).GetProperty(nameof(DriveItem.TrashRootId))!.SetValue(child, folder.Id);

        Should.Throw<DriveException.RestoreContainingFolder>(() => child.Restore(root, ItemName.Create("a.txt")));
        Should.Throw<DriveException.NotInTrash>(() => root.Restore(root, ItemName.Create("x")));
    }

    [Fact]
    public void AddVersion_NumbersIncrease_AndBumpRevisionAfterTheFirst()
    {
        DriveItem file = File(Root());
        file.LastVersionNo.ShouldBe(1);
        file.Revision.ShouldBe(1);

        FileVersion v2 = file.AddVersion(Guid.NewGuid(), Guid.NewGuid(), 100, "text/plain");

        v2.VersionNo.ShouldBe(2);
        file.CurrentVersionId.ShouldBe(v2.Id);
        file.SizeBytes.ShouldBe(100);
        file.Revision.ShouldBe(2);
    }

    [Fact]
    public void VersionsBeyondLimit_KeepsNewest_AndNeverTheCurrentVersion()
    {
        DriveItem file = File(Root());
        var history = new List<FileVersion>(file.Versions);
        for (int i = 0; i < 4; i++)
        {
            history.Add(file.AddVersion(Guid.NewGuid(), Guid.NewGuid(), i, "text/plain"));
        }

        // 5 version (1..5), hiện tại là 5, giữ tối đa 3 ⇒ xoá 1 và 2 (cũ nhất trước).
        IReadOnlyList<FileVersion> excess = file.VersionsBeyondLimit(history, 3);

        excess.Select(v => v.VersionNo).ShouldBe([1, 2]);
        file.VersionsBeyondLimit(history, 1).Select(v => v.VersionNo).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void DeleteVersions_RejectsCurrentVersion_AndRaisesEventWithBlobs()
    {
        DriveItem file = File(Root());
        FileVersion first = file.Versions.Single();
        FileVersion second = file.AddVersion(Guid.NewGuid(), Guid.NewGuid(), 1, "text/plain");
        file.ClearDomainEvents();

        Should.Throw<DriveException.CannotDeleteCurrentVersion>(() => file.DeleteVersions([second]));
        file.DeleteVersions([first]);

        var e = file.GetDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<FileVersionsDeletedDomainEvent>();
        e.VersionIds.ShouldBe([first.Id]);
        e.BlobIds.ShouldBe([first.BlobId]);
    }
}
