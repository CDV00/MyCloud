using System.Collections.Concurrent;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace Storage.Api.ObjectStorage;

/// <summary>Kết nối object storage, đọc từ connection string <c>minio</c> (Aspire): <c>Endpoint=...;AccessKey=...;SecretKey=...</c>.</summary>
public sealed record ObjectStorageConnection(Uri Endpoint, string AccessKey, string SecretKey)
{
    public static ObjectStorageConnection Parse(string connectionString)
    {
        var values = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (!values.TryGetValue("Endpoint", out string? endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || !values.TryGetValue("AccessKey", out string? accessKey) || !values.TryGetValue("SecretKey", out string? secretKey))
        {
            throw new InvalidOperationException("Connection string 'minio' must contain Endpoint, AccessKey and SecretKey.");
        }

        return new ObjectStorageConnection(uri, accessKey, secretKey);
    }

}

public sealed record UploadedPart(int PartNumber, string ETag, long SizeBytes);

/// <summary>
/// Mọi thao tác S3. Code chỉ dùng API S3 chuẩn (AWSSDK) nên đổi MinIO sang S3/R2/SeaweedFS không phải sửa (ADR-011).
/// Hai loại client: <b>nội bộ</b> (service → storage) và <b>ký URL</b> theo gốc URL mà trình duyệt dùng — chữ ký S3 V4
/// gồm cả host nên URL phải được ký đúng host mà trình duyệt sẽ gọi.
/// </summary>
public sealed class S3ObjectStorage : IDisposable
{
    private const string Region = "us-east-1";

    private readonly ObjectStorageConnection _connection;
    private readonly StorageOptions _options;
    private readonly AmazonS3Client _internal;
    private readonly ConcurrentDictionary<string, AmazonS3Client> _signers = new(StringComparer.Ordinal);

    public S3ObjectStorage(ObjectStorageConnection connection, IOptions<StorageOptions> options)
    {
        _connection = connection;
        _options = options.Value;
        _internal = CreateClient(connection.Endpoint);
    }

    public string Bucket => _options.Bucket;

    /// <summary>Tạo bucket nếu chưa có. Trả false nếu không đặt được rule lifecycle (chỉ là lưới an toàn).</summary>
    public async Task<bool> EnsureBucketAsync(CancellationToken ct)
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_internal, Bucket))
        {
            await _internal.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, ct);
        }

        // Multipart bỏ dở bị object storage tự dọn sau 7 ngày (job của service xử lý sớm hơn nhiều).
        // Không phải nhà cung cấp S3-compatible nào cũng hỗ trợ lifecycle ⇒ lỗi ở đây không chặn khởi động.
        try
        {
            await _internal.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
            {
                BucketName = Bucket,
                Configuration = new LifecycleConfiguration
                {
                    Rules =
                    [
                        new LifecycleRule
                        {
                            Id = "abort-incomplete-multipart",
                            Status = LifecycleRuleStatus.Enabled,
                            Filter = new LifecycleFilter { LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = "u/" } },
                            AbortIncompleteMultipartUpload = new LifecycleRuleAbortIncompleteMultipartUpload { DaysAfterInitiation = 7 },
                        },
                    ],
                },
            }, ct);
            return true;
        }
        catch (AmazonS3Exception)
        {
            return false;
        }
    }

    public async Task<string> InitiateMultipartAsync(string key, string contentType, CancellationToken ct)
    {
        InitiateMultipartUploadResponse response = await _internal.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = Bucket, Key = key, ContentType = contentType }, ct);
        return response.UploadId;
    }

    public string PresignPart(string? publicBaseUrl, string key, string uploadId, int partNumber, DateTimeOffset expiresAt)
    {
        (AmazonS3Client signer, Protocol protocol) = SignerFor(publicBaseUrl);
        return signer.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            UploadId = uploadId,
            PartNumber = partNumber,
            Expires = expiresAt.UtcDateTime,
            Protocol = protocol,
        });
    }

    public string PresignDownload(string? publicBaseUrl, string key, string fileName, string contentType, bool inline, DateTimeOffset expiresAt)
    {
        (AmazonS3Client signer, Protocol protocol) = SignerFor(publicBaseUrl);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            Protocol = protocol,
        };
        request.ResponseHeaderOverrides.ContentDisposition = ContentDisposition(fileName, inline);
        request.ResponseHeaderOverrides.ContentType = contentType;
        return signer.GetPreSignedURL(request);
    }

    public async Task<IReadOnlyList<UploadedPart>> ListPartsAsync(string key, string uploadId, CancellationToken ct)
    {
        var parts = new List<UploadedPart>();
        int? marker = null;
        while (true)
        {
            ListPartsResponse page = await _internal.ListPartsAsync(
                new ListPartsRequest { BucketName = Bucket, Key = key, UploadId = uploadId, PartNumberMarker = marker?.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                ct);
            parts.AddRange((page.Parts ?? []).Select(p => new UploadedPart(p.PartNumber ?? 0, p.ETag, p.Size ?? 0)));
            if (page.IsTruncated != true)
            {
                return parts;
            }

            marker = page.NextPartNumberMarker;
        }
    }

    public Task CompleteMultipartAsync(string key, string uploadId, IEnumerable<(int PartNumber, string ETag)> parts, CancellationToken ct) =>
        _internal.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = Bucket,
            Key = key,
            UploadId = uploadId,
            PartETags = parts.OrderBy(p => p.PartNumber).Select(p => new PartETag(p.PartNumber, p.ETag)).ToList(),
        }, ct);

    public async Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct)
    {
        try
        {
            await _internal.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = Bucket, Key = key, UploadId = uploadId }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Đã huỷ / đã hoàn tất / bị lifecycle dọn — coi như xong.
        }
    }

    public async Task<long?> GetSizeAsync(string key, CancellationToken ct)
    {
        try
        {
            GetObjectMetadataResponse head = await _internal.GetObjectMetadataAsync(Bucket, key, ct);
            return head.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct) =>
        _internal.DeleteObjectAsync(Bucket, key, ct);

    public void Dispose()
    {
        _internal.Dispose();
        foreach (AmazonS3Client client in _signers.Values)
        {
            client.Dispose();
        }
    }

    /// <summary><c>attachment; filename="report.pdf"; filename*=UTF-8''b%C3%A1o%20c%C3%A1o.pdf</c> (RFC 6266).</summary>
    public static string ContentDisposition(string fileName, bool inline)
    {
        string type = inline ? "inline" : "attachment";
        var ascii = new StringBuilder(fileName.Length);
        foreach (char c in fileName)
        {
            ascii.Append(c is >= ' ' and <= '~' and not '"' and not '\\' ? c : '_');
        }

        return $"{type}; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }

    private (AmazonS3Client Client, Protocol Protocol) SignerFor(string? requestBaseUrl)
    {
        Uri baseUri = _options.PublicBaseUrl is { Length: > 0 } configured
            ? new Uri(configured)
            : requestBaseUrl is { Length: > 0 } ? new Uri(requestBaseUrl) : _connection.Endpoint;

        string authority = baseUri.GetLeftPart(UriPartial.Authority);
        AmazonS3Client client = _signers.GetOrAdd(authority, a => CreateClient(new Uri(a)));
        return (client, baseUri.Scheme == Uri.UriSchemeHttps ? Protocol.HTTPS : Protocol.HTTP);
    }

    private AmazonS3Client CreateClient(Uri endpoint) =>
        new(
            new BasicAWSCredentials(_connection.AccessKey, _connection.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint.GetLeftPart(UriPartial.Authority),
                ForcePathStyle = true, // /{bucket}/{key}: Vite/nginx proxy theo tiền tố đường dẫn
                AuthenticationRegion = Region,
                UseHttp = endpoint.Scheme == Uri.UriSchemeHttp,
                // SDK v4 mặc định thêm checksum CRC cho mọi request; chỉ bật khi bắt buộc để tương thích MinIO/S3-compatible.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
}
