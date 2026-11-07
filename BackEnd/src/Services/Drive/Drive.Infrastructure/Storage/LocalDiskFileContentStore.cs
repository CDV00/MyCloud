using Drive.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Drive.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Thư mục gốc; đường dẫn tương đối được tính từ ContentRoot của service.</summary>
    public string RootPath { get; set; } = "App_Data/files";
}

/// <summary>
/// Lưu nội dung file xuống ổ đĩa (thay đường dẫn hard-code D:\TestProJects\... của bản cũ).
/// Chỉ dùng tạm cho Phase 0–1; Phase 2 thay bằng Storage service + MinIO.
/// </summary>
internal sealed class LocalDiskFileContentStore : IFileContentStore
{
    private readonly string _root;

    public LocalDiskFileContentStore(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default)
    {
        string key = Guid.CreateVersion7().ToString("N");
        await using var target = new FileStream(ResolvePath(key), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(target, cancellationToken);
        return key;
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        System.IO.File.Delete(ResolvePath(storageKey));
        return Task.CompletedTask;
    }

    private string ResolvePath(string key)
    {
        // Key do service sinh (Guid "N"), nhưng vẫn chặn path traversal phòng khi dữ liệu bị sửa.
        if (key.Length != 32 || !key.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        return Path.Combine(_root, key);
    }
}
