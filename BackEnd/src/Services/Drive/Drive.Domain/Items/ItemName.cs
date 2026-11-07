using System.Text;

namespace Drive.Domain.Items;

/// <summary>
/// Tên item: 1–255 ký tự sau khi trim, không chứa <c>/</c>, <c>\</c>, ký tự điều khiển, không phải "." hay "..".
/// <see cref="Key"/> dùng để kiểm tra trùng tên trong cùng thư mục: chuẩn hoá Unicode NFC + chữ thường, nên
/// "Báo cáo" (dựng sẵn) và "Báo cáo" (tổ hợp dấu) và "BÁO CÁO" được coi là trùng.
/// </summary>
public readonly record struct ItemName
{
    public const int MaxLength = 255;

    private ItemName(string value, string key)
    {
        Value = value;
        Key = key;
    }

    public string Value { get; }

    public string Key { get; }

    public static ItemName Create(string? raw)
    {
        string value = (raw ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        if (value.Length is 0 or > MaxLength
            || value is "." or ".."
            || value.IndexOfAny(['/', '\\']) >= 0
            || value.Any(char.IsControl))
        {
            throw new DriveException.InvalidName(raw ?? string.Empty);
        }

        return new ItemName(value, KeyOf(value));
    }

    public static string KeyOf(string name) => name.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    /// <summary>
    /// Tên thay thế khi trùng: "report.pdf" → "report (1).pdf"; thư mục / file không có phần mở rộng: "Ảnh" → "Ảnh (1)".
    /// </summary>
    public ItemName WithSuffix(int n, bool keepExtension)
    {
        string ext = keepExtension ? Path.GetExtension(Value) : string.Empty;
        if (ext.Length == Value.Length)
        {
            ext = string.Empty; // ".bashrc": không có tên gốc, coi cả chuỗi là tên
        }

        string stem = Value[..^ext.Length];
        string suffix = $" ({n})";
        int room = MaxLength - suffix.Length - ext.Length;
        return Create(string.Concat(stem.Length > room ? stem[..room] : stem, suffix, ext));
    }

    public override string ToString() => Value;
}
