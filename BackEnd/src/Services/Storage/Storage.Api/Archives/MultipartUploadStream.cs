using Storage.Api.ObjectStorage;

namespace Storage.Api.Archives;

/// <summary>
/// Stream chỉ ghi, đẩy dữ liệu lên object storage theo từng part của một S3 multipart upload — tạo tệp zip nhiều GB mà
/// không cần đĩa tạm hay giữ cả tệp trong bộ nhớ (tối đa ~2 part). <see cref="ZipArchive"/> ghi header / central
/// directory bằng <c>Write</c> đồng bộ: các lần ghi đó chỉ vào bộ đệm; I/O mạng chỉ xảy ra trong <c>WriteAsync</c> và
/// <see cref="CompleteAsync"/>.
/// </summary>
internal sealed class MultipartUploadStream(S3ObjectStorage s3, string key, string uploadId, long partSize) : Stream
{
    private readonly List<(int PartNumber, string ETag)> _parts = [];
    private MemoryStream _buffer = new();
    private long _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public long BytesWritten => _written;

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _buffer.Write(buffer);
        _written += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _buffer.Write(buffer.Span);
        _written += buffer.Length;
        if (_buffer.Length >= partSize)
        {
            await FlushPartAsync(cancellationToken);
        }
    }

    /// <summary>Đẩy phần còn lại (part cuối được phép nhỏ hơn 5 MB) và ghép các part.</summary>
    public async Task CompleteAsync(CancellationToken ct)
    {
        if (_buffer.Length > 0 || _parts.Count == 0)
        {
            await FlushPartAsync(ct);
        }

        await s3.CompleteMultipartAsync(key, uploadId, _parts, ct);
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task FlushPartAsync(CancellationToken ct)
    {
        MemoryStream part = _buffer;
        _buffer = new MemoryStream();
        part.Position = 0;
        await using (part)
        {
            int partNumber = _parts.Count + 1;
            string etag = await s3.UploadPartAsync(key, uploadId, partNumber, part, ct);
            _parts.Add((partNumber, etag));
        }
    }
}
