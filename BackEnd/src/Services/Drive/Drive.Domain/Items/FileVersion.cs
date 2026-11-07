using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Items;

/// <summary>Một phiên bản nội dung của file; byte nằm trong blob của Storage service.</summary>
public sealed class FileVersion : Entity<Guid>, IAuditableEntity
{
    private FileVersion()
    {
    }

    public Guid ItemId { get; private set; }

    public int VersionNo { get; private set; }

    public Guid BlobId { get; private set; }

    public long SizeBytes { get; private set; }

    public string MimeType { get; private set; } = string.Empty;

    public DateTimeOffset CreatedOnUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? ModifiedOnUtc { get; set; }

    public Guid? ModifiedBy { get; set; }

    internal static FileVersion Create(Guid id, Guid itemId, int versionNo, Guid blobId, long sizeBytes, string mimeType) =>
        new()
        {
            Id = id,
            ItemId = itemId,
            VersionNo = versionNo,
            BlobId = blobId,
            SizeBytes = sizeBytes,
            MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType.Trim(),
        };
}
