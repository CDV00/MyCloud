using Drive.Domain;
using Drive.Domain.Items;

namespace Drive.Application.Items;

/// <summary>Chọn tên chưa dùng trong thư mục: "report.pdf" → "report (1).pdf" → "report (2).pdf"...</summary>
public sealed class UniqueNames(IDriveItemRepository items)
{
    public async Task<ItemName> ResolveAsync(Guid parentId, ItemName desired, bool isFile, CancellationToken ct)
    {
        if (!await items.NameExistsAsync(parentId, desired.Key, exceptItemId: null, ct))
        {
            return desired;
        }

        string stemKey = ItemName.KeyOf(isFile && Path.GetExtension(desired.Value) is { Length: > 0 } ext && ext.Length < desired.Value.Length
            ? desired.Value[..^ext.Length]
            : desired.Value);
        IReadOnlySet<string> taken = await items.NameKeysStartingWithAsync(parentId, stemKey, ct);

        for (int n = 1; n < 10_000; n++)
        {
            ItemName candidate = desired.WithSuffix(n, keepExtension: isFile);
            if (!taken.Contains(candidate.Key))
            {
                return candidate;
            }
        }

        throw new DriveException.NameConflict(desired.Value);
    }
}
