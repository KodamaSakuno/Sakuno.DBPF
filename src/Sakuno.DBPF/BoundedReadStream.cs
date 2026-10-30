namespace Sakuno.DBPF;

internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _stream;
    private readonly object _syncRoot;
    private readonly long _start;
    private readonly long _length;
    private long _position;
    private bool _disposed;

    internal BoundedReadStream(Stream stream, object syncRoot, long start, long length)
    {
        _stream = stream;
        _syncRoot = syncRoot;
        _start = start;
        _length = length;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _position;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _length);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var remaining = _length - _position;
        if (remaining == 0 || buffer.Length == 0)
            return 0;

        var bytesToRead = (int)Math.Min(buffer.Length, remaining);
        lock (_syncRoot)
        {
            _stream.Position = _start + _position;
            var bytesRead = _stream.Read(buffer[..bytesToRead]);
            _position += bytesRead;
            return bytesRead;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long newPosition;
        try
        {
            newPosition = checked(origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            });
        }
        catch (OverflowException exception)
        {
            throw new IOException("The requested resource position is outside the supported range.", exception);
        }

        if (newPosition < 0 || newPosition > _length)
            throw new IOException("The requested resource position is outside the resource bounds.");

        _position = newPosition;
        return _position;
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing) => _disposed = true;
}
