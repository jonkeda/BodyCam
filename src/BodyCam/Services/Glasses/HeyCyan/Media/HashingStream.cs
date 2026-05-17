using System.Security.Cryptography;

namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Wraps a stream to compute SHA-256 while reading/copying.
/// </summary>
internal sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;
    private bool _disposed;
    private byte[]? _finalHash;

    public HashingStream(Stream inner)
    {
        _inner = inner;
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    public string GetHashHex()
    {
        if (_finalHash is null)
        {
            throw new InvalidOperationException("Must dispose stream before getting hash");
        }
        return Convert.ToHexString(_finalHash).ToLowerInvariant();
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            _hash.AppendData(buffer, offset, read);
        }
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = await _inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
        if (read > 0)
        {
            _hash.AppendData(buffer, offset, read);
        }
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (read > 0)
        {
            _hash.AppendData(buffer.Span[..read]);
        }
        return read;
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _finalHash = _hash.GetHashAndReset();
            _hash.Dispose();
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _finalHash = _hash.GetHashAndReset();
            _hash.Dispose();
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
