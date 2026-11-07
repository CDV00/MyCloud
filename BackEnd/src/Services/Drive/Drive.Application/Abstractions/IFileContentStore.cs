namespace Drive.Application.Abstractions;

/// <summary>
/// Nơi lưu nội dung file. Phase 0: ổ đĩa local (thư mục cấu hình được).
/// Phase 2: thay bằng Storage service + MinIO, client upload trực tiếp bằng presigned URL (ADR-002).
/// </summary>
public interface IFileContentStore
{
    /// <returns>Khoá lưu trữ ngẫu nhiên, không chứa tên file của người dùng.</returns>
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
