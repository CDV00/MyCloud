using Drive.Domain.Items;

namespace Drive.Domain.Tests;

public sealed class DriveItemTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static DriveItem Root() => DriveItem.CreateRoot(Guid.NewGuid(), Owner);

    private static DriveItem Folder(DriveItem parent, string name = "A") =>
        DriveItem.CreateFolder(Guid.NewGuid(), parent, ItemName.Create(name));

    private static DriveItem File(DriveItem parent, string name = "a.txt") =>
        DriveItem.CreateFile(Guid.NewGuid(), parent, ItemName.Create(name), Guid.NewGuid(), Guid.NewGuid(), 42, "text/plain");

    [Fact]
    public void CreateRoot_IsFolderWithoutParent_AndRaisesCreated()
    {
        DriveItem root = Root();

        root.IsRoot.ShouldBeTrue();
        root.ParentId.ShouldBeNull();
        root.Kind.ShouldBe(ItemKind.Folder);
        root.Path.ShouldBe($"/{root.Id:N}/");
        root.Revision.ShouldBe(1);
        root.GetDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<ItemCreatedDomainEvent>();
    }

    [Fact]
    public void CreateRoot_WithoutOwner_Throws()
    {
        Should.Throw<ArgumentException>(() => DriveItem.CreateRoot(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void Child_InheritsOwner_AndExtendsPath()
    {
        DriveItem root = Root();
        DriveItem folder = Folder(root);
        DriveItem nested = Folder(folder, "B");

        nested.OwnerId.ShouldBe(Owner);
        nested.Path.ShouldBe($"/{root.Id:N}/{folder.Id:N}/{nested.Id:N}/");
        nested.Depth.ShouldBe(2);
        nested.AncestorIds.ShouldBe([root.Id, folder.Id]);
    }

    [Fact]
    public void CreateFile_AddsFirstVersion_AndRaisesCreatedThenVersionAdded()
    {
        DriveItem file = File(Root());

        file.Kind.ShouldBe(ItemKind.File);
        file.SizeBytes.ShouldBe(42);
        file.MimeType.ShouldBe("text/plain");
        FileVersion version = file.Versions.ShouldHaveSingleItem();
        version.VersionNo.ShouldBe(1);
        file.CurrentVersionId.ShouldBe(version.Id);
        file.GetDomainEvents().Select(e => e.GetType())
            .ShouldBe([typeof(ItemCreatedDomainEvent), typeof(FileVersionAddedDomainEvent)]);
    }

    [Fact]
    public void Child_OfFile_IsRejected()
    {
        DriveItem file = File(Root());

        Should.Throw<DriveException.NotAFolder>(() => Folder(file));
    }

    [Fact]
    public void Rename_IncrementsRevision_AndRaisesRenamed()
    {
        DriveItem folder = Folder(Root());
        folder.ClearDomainEvents();

        folder.Rename(ItemName.Create("B"), expectedRevision: 1);

        folder.Name.ShouldBe("B");
        folder.Revision.ShouldBe(2);
        folder.GetDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<ItemRenamedDomainEvent>();
    }

    [Fact]
    public void Rename_WithStaleRevision_IsRejected()
    {
        DriveItem folder = Folder(Root());
        folder.Rename(ItemName.Create("B"), null);

        Should.Throw<DriveException.RevisionMismatch>(() => folder.Rename(ItemName.Create("C"), expectedRevision: 1));
    }

    [Fact]
    public void Rename_Root_IsRejected()
    {
        Should.Throw<DriveException.RootImmutable>(() => Root().Rename(ItemName.Create("x"), null));
    }

    [Fact]
    public void MoveTo_UpdatesPathAndDepth_AndReturnsSubtreeChange()
    {
        DriveItem root = Root();
        DriveItem source = Folder(root, "src");
        DriveItem target = Folder(root, "dst");
        DriveItem moving = Folder(source, "m");
        string oldPath = moving.Path;

        SubtreeMove? move = moving.MoveTo(target, null);

        moving.ParentId.ShouldBe(target.Id);
        moving.Path.ShouldBe($"{target.Path}{moving.Id:N}/");
        moving.Revision.ShouldBe(2);
        move.ShouldNotBeNull();
        move.OldPath.ShouldBe(oldPath);
        move.NewPath.ShouldBe(moving.Path);
        move.DepthDelta.ShouldBe(0);
        moving.GetDomainEvents().OfType<ItemMovedDomainEvent>().ShouldHaveSingleItem().OldParentId.ShouldBe(source.Id);
    }

    [Fact]
    public void MoveTo_SameParent_IsNoOp()
    {
        DriveItem root = Root();
        DriveItem folder = Folder(root);

        folder.MoveTo(root, null).ShouldBeNull();
        folder.Revision.ShouldBe(1);
    }

    [Fact]
    public void MoveTo_ItselfOrDescendant_IsRejected()
    {
        DriveItem root = Root();
        DriveItem parent = Folder(root, "p");
        DriveItem child = Folder(parent, "c");
        DriveItem grandChild = Folder(child, "g");

        Should.Throw<DriveException.InvalidMove>(() => parent.MoveTo(parent, null));
        Should.Throw<DriveException.InvalidMove>(() => parent.MoveTo(grandChild, null));
    }

    [Fact]
    public void MoveTo_File_IsRejected()
    {
        DriveItem root = Root();
        DriveItem folder = Folder(root);

        Should.Throw<DriveException.NotAFolder>(() => folder.MoveTo(File(root), null));
    }
}
