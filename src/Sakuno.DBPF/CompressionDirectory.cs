using System.Buffers.Binary;

namespace Sakuno.DBPF;

internal static class CompressionDirectory
{
    private const int EntrySize = 16;

    internal static readonly Tgi Identifier = new(0xE86B1EEF, 0xE86B1EEF, 0x286B1F03);

    internal static Dictionary<Tgi, uint> Read(
        Stream stream,
        IReadOnlyDictionary<Tgi, DbpfIndexEntry> archiveEntries)
    {
        var sizes = new Dictionary<Tgi, uint>();
        if (!archiveEntries.TryGetValue(Identifier, out var directory))
            return sizes;

        if (directory.Size % EntrySize != 0)
            throw new InvalidDataException($"The DBPF compression directory size {directory.Size} is not a multiple of {EntrySize}.");

        stream.Position = directory.Offset;
        Span<byte> entryBytes = stackalloc byte[EntrySize];
        var entryCount = directory.Size / EntrySize;

        for (var index = 0u; index < entryCount; index++)
        {
            try
            {
                stream.ReadExactly(entryBytes);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException($"The DBPF compression directory is truncated at entry {index}.", exception);
            }

            var identifier = new Tgi(
                ReadUInt32(entryBytes, 0),
                ReadUInt32(entryBytes, 4),
                ReadUInt32(entryBytes, 8));

            if (identifier == Identifier)
                throw new InvalidDataException($"The DBPF compression directory incorrectly marks itself as compressed at entry {index}.");
            if (!archiveEntries.ContainsKey(identifier))
                throw new InvalidDataException($"The DBPF compression directory references missing resource {identifier} at entry {index}.");
            if (!sizes.TryAdd(identifier, ReadUInt32(entryBytes, 12)))
                throw new InvalidDataException($"The DBPF compression directory contains duplicate resource {identifier} at entry {index}.");
        }

        return sizes;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
}
