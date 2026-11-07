using Drive.Domain.Items;

namespace Drive.Application.Items;

/// <summary>Item trả về cho client. <c>Revision</c> cũng được trả trong header <c>ETag</c> để dùng với <c>If-Match</c>.</summary>
public sealed record ItemDto(
    Guid Id,
    Guid? ParentId,
    string Kind,
    string Name,
    string? MimeType,
    long SizeBytes,
    bool IsRoot,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt)
{
    public static ItemDto From(DriveItem item) =>
        new(
            item.Id,
            item.ParentId,
            item.IsFolder ? "folder" : "file",
            item.Name,
            item.MimeType,
            item.SizeBytes,
            item.IsRoot,
            item.Revision,
            item.CreatedOnUtc,
            item.ModifiedOnUtc ?? item.CreatedOnUtc);
}

public sealed record ItemPage(IReadOnlyList<ItemDto> Items, string? NextPageToken);
