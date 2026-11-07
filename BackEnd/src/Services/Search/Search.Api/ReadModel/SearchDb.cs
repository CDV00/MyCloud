using System.Globalization;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Search.Api.ReadModel;

/// <summary>
/// Bản sao rút gọn của DriveItem để tìm kiếm (docs/03 §6). <see cref="AncestorIds"/> cho phép ẩn cả cây con của một
/// thư mục trong thùng rác mà chỉ cần đánh dấu chính thư mục đó (<see cref="Trashed"/>).
/// </summary>
[BsonIgnoreExtraElements]
public sealed class ItemDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>Từ gốc tới cha trực tiếp.</summary>
    public List<Guid> AncestorIds { get; set; } = [];

    /// <summary>"folder" | "file".</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Tên chữ thường để tìm không phân biệt hoa thường.</summary>
    public string NameLower { get; set; } = string.Empty;

    /// <summary>Tên bỏ dấu tiếng Việt ("Báo cáo" ⇒ "bao cao") để gõ không dấu vẫn tìm thấy.</summary>
    public string NameFolded { get; set; } = string.Empty;

    /// <summary>folder | image | video | audio | pdf | doc | sheet | slide | archive | other.</summary>
    public string Category { get; set; } = Categories.Other;

    public string? MimeType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Revision của item trong Drive — chỉ áp dụng event có revision mới hơn (bỏ qua event cũ/lặp).</summary>
    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    /// <summary>Chỉ đúng với item người dùng xoá (gốc nhóm); con cháu bị ẩn nhờ <see cref="AncestorIds"/>.</summary>
    public bool Trashed { get; set; }

    public DateTimeOffset? TrashedAt { get; set; }
}

/// <summary>"Gần đây": lần cuối mỗi người mở / tải / tải lên một file. TTL 90 ngày.</summary>
public sealed class RecentDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public Guid ItemId { get; set; }

    public DateTimeOffset LastAccessAt { get; set; }

    /// <summary>open | download | upload | edit.</summary>
    public string Action { get; set; } = string.Empty;

    public static string KeyOf(Guid userId, Guid itemId) => $"{userId:N}:{itemId:N}";
}

/// <summary>Nhật ký hoạt động của item (TTL 180 ngày). Id = id event ⇒ ghi lặp khi event được giao lại là vô hại.</summary>
[BsonIgnoreExtraElements]
public sealed class ActivityDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>Tổ tiên của item lúc xảy ra — hoạt động của thư mục gồm cả hoạt động của các item bên trong.</summary>
    public List<Guid> AncestorIds { get; set; } = [];

    public Guid ActorId { get; set; }

    /// <summary>created | renamed | moved | trashed | restored | version_added.</summary>
    public string Action { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>Tên item tại thời điểm đó.</summary>
    public string Name { get; set; } = string.Empty;

    public string? PreviousName { get; set; }

    /// <summary>Tên thư mục nguồn / đích khi di chuyển; null = "Drive của tôi" (hoặc chưa biết).</summary>
    public string? FromFolder { get; set; }

    public string? ToFolder { get; set; }

    public int? VersionNo { get; set; }
}

public sealed class SearchDb(IMongoDatabase database)
{
    public static readonly TimeSpan RecentTtl = TimeSpan.FromDays(90);
    public static readonly TimeSpan ActivityTtl = TimeSpan.FromDays(180);

    public IMongoCollection<ItemDocument> Items { get; } = database.GetCollection<ItemDocument>("items");

    public IMongoCollection<RecentDocument> Recent { get; } = database.GetCollection<RecentDocument>("recent");

    public IMongoCollection<ActivityDocument> Activity { get; } = database.GetCollection<ActivityDocument>("activity");

    /// <summary>Guid lưu dạng UUID chuẩn; DateTimeOffset lưu dạng ngày giờ (so sánh được, dùng được cho TTL index).</summary>
    public static void RegisterSerializers()
    {
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var items = Builders<ItemDocument>.IndexKeys;
        await Items.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ItemDocument>(items.Ascending(x => x.OwnerId).Ascending(x => x.ParentId)),
                new CreateIndexModel<ItemDocument>(items.Ascending(x => x.OwnerId).Ascending(x => x.NameFolded)),
                new CreateIndexModel<ItemDocument>(items.Ascending(x => x.OwnerId).Descending(x => x.UpdatedAt)),
                new CreateIndexModel<ItemDocument>(items.Ascending(x => x.OwnerId).Ascending(x => x.Trashed)),
                new CreateIndexModel<ItemDocument>(items.Ascending(x => x.AncestorIds)),
            ],
            cancellationToken);

        var recent = Builders<RecentDocument>.IndexKeys;
        await Recent.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RecentDocument>(recent.Ascending(x => x.UserId).Descending(x => x.LastAccessAt)),
                new CreateIndexModel<RecentDocument>(recent.Ascending(x => x.ItemId)),
                new CreateIndexModel<RecentDocument>(recent.Ascending(x => x.LastAccessAt), new CreateIndexOptions { ExpireAfter = RecentTtl }),
            ],
            cancellationToken);

        var activity = Builders<ActivityDocument>.IndexKeys;
        await Activity.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ActivityDocument>(activity.Ascending(x => x.ItemId).Descending(x => x.At)),
                new CreateIndexModel<ActivityDocument>(activity.Ascending(x => x.AncestorIds).Descending(x => x.At)),
                new CreateIndexModel<ActivityDocument>(activity.Ascending(x => x.At), new CreateIndexOptions { ExpireAfter = ActivityTtl }),
            ],
            cancellationToken);

        // Collection của Phase 0–1 (mô hình Folder/File) không còn dùng.
        await database.DropCollectionAsync("folders", cancellationToken);
        await database.DropCollectionAsync("files", cancellationToken);
    }
}

/// <summary>Nhóm loại file cho bộ lọc tìm kiếm.</summary>
public static class Categories
{
    public const string Folder = "folder";
    public const string Image = "image";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Pdf = "pdf";
    public const string Document = "doc";
    public const string Spreadsheet = "sheet";
    public const string Presentation = "slide";
    public const string Archive = "archive";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Folder, Image, Video, Audio, Pdf, Document, Spreadsheet, Presentation, Archive, Other,
    };

    public static string Of(bool isFolder, string? mimeType)
    {
        if (isFolder)
        {
            return Folder;
        }

        string mime = (mimeType ?? string.Empty).ToLowerInvariant();
        return mime switch
        {
            _ when mime.StartsWith("image/", StringComparison.Ordinal) => Image,
            _ when mime.StartsWith("video/", StringComparison.Ordinal) => Video,
            _ when mime.StartsWith("audio/", StringComparison.Ordinal) => Audio,
            "application/pdf" => Pdf,
            _ when mime.Contains("spreadsheet", StringComparison.Ordinal) || mime.Contains("excel", StringComparison.Ordinal)
                || mime == "text/csv" => Spreadsheet,
            _ when mime.Contains("presentation", StringComparison.Ordinal) || mime.Contains("powerpoint", StringComparison.Ordinal) => Presentation,
            _ when mime.Contains("wordprocessing", StringComparison.Ordinal) || mime == "application/msword"
                || mime.Contains("opendocument.text", StringComparison.Ordinal) || mime == "application/rtf"
                || mime.StartsWith("text/", StringComparison.Ordinal) => Document,
            _ when mime.Contains("zip", StringComparison.Ordinal) || mime.Contains("compressed", StringComparison.Ordinal)
                || mime.Contains("x-tar", StringComparison.Ordinal) || mime.Contains("x-rar", StringComparison.Ordinal)
                || mime.Contains("x-7z", StringComparison.Ordinal) || mime == "application/gzip" => Archive,
            _ => Other,
        };
    }
}

/// <summary>Chuẩn hoá để tìm không dấu: chữ thường, bỏ dấu thanh/dấu mũ, "đ" ⇒ "d".</summary>
public static class TextFolding
{
    public static string Fold(string text)
    {
        string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c == 'đ' ? 'd' : c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
