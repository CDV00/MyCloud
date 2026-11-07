using Drive.Domain.Entities;
using Drive.Domain.Events;
using Drive.Domain.Exceptions;

namespace Drive.Domain.Tests;

public sealed class FolderTests
{
    [Fact]
    public void Create_ValidName_TrimsNameAndRaisesCreatedEvent()
    {
        var id = Guid.NewGuid();

        var folder = Folder.Create(id, "  Báo cáo 2026  ", null, parentId: null);

        folder.Name.ShouldBe("Báo cáo 2026");
        folder.Description.ShouldBeEmpty();
        folder.GetDomainEvents().ShouldHaveSingleItem()
            .ShouldBeOfType<FolderCreatedDomainEvent>()
            .FolderId.ShouldBe(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("tab\tname")]
    public void Create_InvalidName_Throws(string name)
    {
        Should.Throw<FolderException.InvalidName>(() => Folder.Create(Guid.NewGuid(), name, null, null));
    }

    [Fact]
    public void Create_NameLongerThan255_Throws()
    {
        // Bản cũ giới hạn 50 ở domain nhưng 200 ở DB — giờ thống nhất 255.
        Should.Throw<FolderException.InvalidName>(() => Folder.Create(Guid.NewGuid(), new string('a', 256), null, null));
        Should.NotThrow(() => Folder.Create(Guid.NewGuid(), new string('a', 255), null, null));
    }

    [Fact]
    public void Update_SelfAsParent_Throws()
    {
        var folder = Folder.Create(Guid.NewGuid(), "A", null, null);

        Should.Throw<FolderException.InvalidParent>(() => folder.Update("A", null, folder.Id));
    }

    [Fact]
    public void Update_RaisesUpdatedEvent()
    {
        var folder = Folder.Create(Guid.NewGuid(), "A", null, null);
        folder.ClearDomainEvents();

        folder.Update("B", "mô tả", Guid.NewGuid());

        folder.Name.ShouldBe("B");
        folder.GetDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<FolderUpdatedDomainEvent>();
    }
}
