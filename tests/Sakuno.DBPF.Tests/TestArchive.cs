using System.Buffers.Binary;

namespace Sakuno.DBPF.Tests;

internal static class TestArchive
{
    internal const int HeaderSize = 96;
    internal const int IndexEntrySize = 20;

    internal static byte[] Create(params (Tgi Identifier, byte[] Data)[] resources)
    {
        var dataSize = resources.Sum(resource => resource.Data.Length);
        var indexOffset = HeaderSize + dataSize;
        var bytes = new byte[indexOffset + resources.Length * IndexEntrySize];
        "DBPF"u8.CopyTo(bytes);
        WriteUInt32(bytes, 0x04, 1);
        WriteUInt32(bytes, 0x18, 0x01020304);
        WriteUInt32(bytes, 0x1C, 0x05060708);
        WriteUInt32(bytes, 0x20, 7);
        WriteUInt32(bytes, 0x24, (uint)resources.Length);
        WriteUInt32(bytes, 0x28, (uint)indexOffset);
        WriteUInt32(bytes, 0x2C, (uint)(resources.Length * IndexEntrySize));

        var resourceOffset = HeaderSize;
        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index];
            resource.Data.CopyTo(bytes, resourceOffset);

            var entryOffset = indexOffset + index * IndexEntrySize;
            WriteUInt32(bytes, entryOffset, resource.Identifier.Type);
            WriteUInt32(bytes, entryOffset + 4, resource.Identifier.Group);
            WriteUInt32(bytes, entryOffset + 8, resource.Identifier.Instance);
            WriteUInt32(bytes, entryOffset + 12, (uint)resourceOffset);
            WriteUInt32(bytes, entryOffset + 16, (uint)resource.Data.Length);
            resourceOffset += resource.Data.Length;
        }

        return bytes;
    }

    internal static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
