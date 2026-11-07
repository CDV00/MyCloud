using Drive.Domain.Events;
using Drive.Domain.Exceptions;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Domain.Tests;

public sealed class FileTests
{
    private static DriveFile NewFile() =>
        DriveFile.Create(Guid.NewGuid(), "report.pdf", "0123456789abcdef0123456789abcdef", "application/pdf", 1024, null, Guid.NewGuid());

    [Fact]
    public void Create_WithoutContent_Throws()
    {
        Should.Throw<FileException.MissingContent>(() =>
            DriveFile.Create(Guid.NewGuid(), "a.txt", storageKey: "", "text/plain", 1, null, Guid.NewGuid()));
    }

    [Fact]
    public void Create_EmptyContentType_DefaultsToOctetStream()
    {
        var file = DriveFile.Create(Guid.NewGuid(), "a.bin", "0123456789abcdef0123456789abcdef", "", 1, null, Guid.NewGuid());

        file.ContentType.ShouldBe("application/octet-stream");
    }

    [Fact]
    public void UpdateMetadata_KeepsStorageKeyAndSize()
    {
        // Hồi quy lỗi S6: bản cũ ghi đè StoredName = "" khi cập nhật → mất liên kết tới nội dung.
        DriveFile file = NewFile();
        string key = file.StorageKey;
        var newFolder = Guid.NewGuid();

        file.UpdateMetadata("renamed.pdf", "ghi chú", newFolder);

        file.StorageKey.ShouldBe(key);
        file.SizeBytes.ShouldBe(1024);
        file.Name.ShouldBe("renamed.pdf");
        file.FolderId.ShouldBe(newFolder);
    }

    [Fact]
    public void Delete_RaisesEventWithStorageKey()
    {
        DriveFile file = NewFile();
        file.ClearDomainEvents();

        file.Delete();

        var e = file.GetDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<FileDeletedDomainEvent>();
        e.FileId.ShouldBe(file.Id);
        e.StorageKey.ShouldBe(file.StorageKey);
    }
}
