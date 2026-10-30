using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace Sakuno.DBPF;

/// <summary>Provides read-only access to the index and raw resources of a DBPF 1.0 archive.</summary>
public sealed class DbpfArchive : IDisposable
{
    private const int HeaderSize = 96;
    private const int IndexEntrySize = 20;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly object _syncRoot = new();
    private readonly Dictionary<Tgi, DbpfIndexEntry> _entriesByTgi;
    private bool _disposed;

    private DbpfArchive(
        Stream stream,
        bool leaveOpen,
        DbpfHeader header,
        List<DbpfIndexEntry> entries,
        Dictionary<Tgi, DbpfIndexEntry> entriesByTgi)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        Header = header;
        Entries = new ReadOnlyCollection<DbpfIndexEntry>(entries);
        _entriesByTgi = entriesByTgi;
    }

    public DbpfHeader Header { get; }
    public IReadOnlyList<DbpfIndexEntry> Entries { get; }

    public static DbpfArchive Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The DBPF stream must be readable.", nameof(stream));
        if (!stream.CanSeek)
            throw new ArgumentException("The DBPF stream must be seekable.", nameof(stream));
        if (stream.Length < HeaderSize)
            throw new InvalidDataException($"The DBPF header is truncated: expected {HeaderSize} bytes, found {stream.Length}.");

        Span<byte> headerBytes = stackalloc byte[HeaderSize];
        stream.Position = 0;
        ReadExactly(stream, headerBytes, "header");

        if (!headerBytes[..4].SequenceEqual("DBPF"u8))
            throw new InvalidDataException("The stream does not start with the DBPF magic value.");

        var header = new DbpfHeader(
            MajorVersion: ReadUInt32(headerBytes, 0x04),
            MinorVersion: ReadUInt32(headerBytes, 0x08),
            CreatedTimestamp: ReadUInt32(headerBytes, 0x18),
            ModifiedTimestamp: ReadUInt32(headerBytes, 0x1C),
            IndexMajorVersion: ReadUInt32(headerBytes, 0x20),
            IndexEntryCount: ReadUInt32(headerBytes, 0x24),
            IndexOffset: ReadUInt32(headerBytes, 0x28),
            IndexSize: ReadUInt32(headerBytes, 0x2C),
            HoleEntryCount: ReadUInt32(headerBytes, 0x30),
            HoleOffset: ReadUInt32(headerBytes, 0x34),
            HoleSize: ReadUInt32(headerBytes, 0x38),
            IndexMinorVersion: ReadUInt32(headerBytes, 0x3C));

        if (header.MajorVersion != 1 || header.MinorVersion != 0)
        {
            throw new NotSupportedException(
                $"DBPF version {header.MajorVersion}.{header.MinorVersion} is not supported; only version 1.0 is supported.");
        }

        if (header.IndexMajorVersion != 7 || header.IndexMinorVersion != 0)
        {
            throw new NotSupportedException(
                $"DBPF index version {header.IndexMajorVersion}.{header.IndexMinorVersion} is not supported; only version 7.0 is supported.");
        }

        if (header.IndexEntryCount > int.MaxValue)
            throw new NotSupportedException("The DBPF index contains more entries than this reader can address.");

        ValidateTable("regular index", header.IndexEntryCount, header.IndexOffset, header.IndexSize, stream.Length);
        ValidateTable("hole table", header.HoleEntryCount, header.HoleOffset, header.HoleSize, stream.Length);

        var entries = new List<DbpfIndexEntry>((int)header.IndexEntryCount);
        var entriesByTgi = new Dictionary<Tgi, DbpfIndexEntry>((int)header.IndexEntryCount);
        stream.Position = header.IndexOffset;
        Span<byte> entryBytes = stackalloc byte[IndexEntrySize];

        for (var index = 0; index < (int)header.IndexEntryCount; index++)
        {
            ReadExactly(stream, entryBytes, $"regular index entry {index}");
            var identifier = new Tgi(
                ReadUInt32(entryBytes, 0x00),
                ReadUInt32(entryBytes, 0x04),
                ReadUInt32(entryBytes, 0x08));
            var entry = new DbpfIndexEntry(
                identifier,
                ReadUInt32(entryBytes, 0x0C),
                ReadUInt32(entryBytes, 0x10));

            ValidateResourceRange(entry, index, stream.Length);
            if (!entriesByTgi.TryAdd(identifier, entry))
                throw new InvalidDataException($"The regular index contains duplicate TGI {identifier} at entry {index}.");

            entries.Add(entry);
        }

        return new DbpfArchive(stream, leaveOpen, header, entries, entriesByTgi);
    }

    public DbpfIndexEntry GetEntry(Tgi identifier)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _entriesByTgi.TryGetValue(identifier, out var entry)
            ? entry
            : throw new KeyNotFoundException($"The DBPF archive does not contain resource {identifier}.");
    }

    /// <summary>Opens a bounded stream over the resource's stored bytes without decompressing them.</summary>
    public Stream OpenRead(Tgi identifier)
    {
        var entry = GetEntry(identifier);
        return new BoundedReadStream(_stream, _syncRoot, entry.Offset, entry.Size);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_leaveOpen)
            _stream.Dispose();
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);

    private static void ReadExactly(Stream stream, Span<byte> buffer, string description)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"The DBPF {description} is truncated.", exception);
        }
    }

    private static void ValidateTable(string description, uint count, uint offset, uint size, long streamLength)
    {
        var expectedSize = (ulong)count * IndexEntrySize;
        if (size != expectedSize)
        {
            throw new InvalidDataException(
                $"The DBPF {description} has size {size}, but {count} entries require {expectedSize} bytes.");
        }

        ValidateRange(description, offset, size, streamLength);
    }

    private static void ValidateResourceRange(DbpfIndexEntry entry, int index, long streamLength) =>
        ValidateRange($"resource at regular index entry {index} ({entry.Identifier})", entry.Offset, entry.Size, streamLength);

    private static void ValidateRange(string description, uint offset, uint size, long streamLength)
    {
        if (offset > streamLength || size > streamLength - offset)
        {
            throw new InvalidDataException(
                $"The DBPF {description} range (offset {offset}, size {size}) exceeds the stream length {streamLength}.");
        }
    }
}
