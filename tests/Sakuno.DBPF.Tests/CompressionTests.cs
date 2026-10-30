namespace Sakuno.DBPF.Tests;

public sealed class CompressionTests
{
    private static readonly Tgi ResourceTgi = new(0x11223344, 0x55667788, 0x99AABBCC);
    private static readonly Tgi OtherTgi = new(0x12345678, 0x23456789, 0x3456789A);
    private static readonly Tgi DirectoryTgi = new(0xE86B1EEF, 0xE86B1EEF, 0x286B1F03);

    [Fact]
    public void Open_ReadsCompressionMetadataAndPreservesRawAccess()
    {
        var payload = CreatePayload([0xE0, 1, 2, 3, 4, 0xFC], 4);
        using var archive = OpenCompressed(payload, 4);
        var entry = archive.GetEntry(ResourceTgi);

        Assert.True(entry.IsCompressed);
        Assert.Equal(4u, entry.UncompressedSize);
        Assert.False(archive.GetEntry(DirectoryTgi).IsCompressed);

        using var raw = archive.OpenRead(ResourceTgi);
        using var decompressed = archive.OpenDecompressed(ResourceTgi);
        Assert.Equal(payload, ReadAllBytes(raw));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ReadAllBytes(decompressed));
    }

    [Fact]
    public void OpenDecompressed_UncompressedResourceReturnsStoredBytesWithoutSignatureDetection()
    {
        byte[] data = [0x10, 0xFB, 1, 2, 3];
        using var archive = DbpfArchive.Open(new MemoryStream(TestArchive.Create((ResourceTgi, data))));

        Assert.False(archive.GetEntry(ResourceTgi).IsCompressed);
        using var stream = archive.OpenDecompressed(ResourceTgi);
        Assert.Equal(data, ReadAllBytes(stream));
    }

    [Fact]
    public void OpenDecompressed_SupportsEveryCommandEncoding()
    {
        var cases = new (byte[] Commands, byte[] Expected)[]
        {
            ([0x0E, 0x01, 0x41, 0x42, 0xFC], "ABABABAB"u8.ToArray()),
            ([0x80, 0x80, 0x01, 0x41, 0x42, 0xFC], "ABABAB"u8.ToArray()),
            ([0xC2, 0x00, 0x01, 0x00, 0x41, 0x42, 0xFC], "ABABABA"u8.ToArray()),
            ([0xE0, 0x41, 0x42, 0x43, 0x44, 0xFC], "ABCD"u8.ToArray()),
            ([0xFF, 0x41, 0x42, 0x43], "ABC"u8.ToArray()),
            ([0xFC], []),
        };

        foreach (var testCase in cases)
        {
            var payload = CreatePayload(testCase.Commands, testCase.Expected.Length);
            using var archive = OpenCompressed(payload, (uint)testCase.Expected.Length);
            using var stream = archive.OpenDecompressed(ResourceTgi);
            Assert.Equal(testCase.Expected, ReadAllBytes(stream));
        }
    }

    [Fact]
    public void OpenDecompressed_SupportsMaximumLongLiteralRun()
    {
        var expected = Enumerable.Range(0, 112).Select(value => (byte)value).ToArray();
        var commands = new byte[] { 0xFB }.Concat(expected).Append((byte)0xFC).ToArray();
        var payload = CreatePayload(commands, expected.Length);
        using var archive = OpenCompressed(payload, (uint)expected.Length);

        using var stream = archive.OpenDecompressed(ResourceTgi);
        Assert.Equal(expected, ReadAllBytes(stream));
    }

    [Fact]
    public void Open_PartialCompressionDirectoryThrows()
    {
        var payload = CreatePayload([0xFC], 0);
        var directory = new byte[15];

        var exception = Assert.Throws<InvalidDataException>(() => OpenCompressed(payload, 0, directory));
        Assert.Contains("not a multiple", exception.Message);
    }

    [Fact]
    public void Open_CompressionDirectoryWithMissingResourceThrows()
    {
        var payload = CreatePayload([0xFC], 0);
        var directory = CreateDirectory((OtherTgi, 0));

        var exception = Assert.Throws<InvalidDataException>(() => OpenCompressed(payload, 0, directory));
        Assert.Contains("missing resource", exception.Message);
        Assert.Contains(OtherTgi.ToString(), exception.Message);
    }

    [Fact]
    public void Open_CompressionDirectoryWithDuplicateResourceThrows()
    {
        var payload = CreatePayload([0xFC], 0);
        var directory = CreateDirectory((ResourceTgi, 0), (ResourceTgi, 0));

        var exception = Assert.Throws<InvalidDataException>(() => OpenCompressed(payload, 0, directory));
        Assert.Contains("duplicate resource", exception.Message);
    }

    [Fact]
    public void Open_CompressionDirectoryMarkingItselfThrows()
    {
        var payload = CreatePayload([0xFC], 0);
        var directory = CreateDirectory((DirectoryTgi, 0));

        var exception = Assert.Throws<InvalidDataException>(() => OpenCompressed(payload, 0, directory));
        Assert.Contains("itself", exception.Message);
    }

    [Fact]
    public void OpenDecompressed_StoredSizeMismatchThrows()
    {
        var payload = CreatePayload([0xFC], 0);
        TestArchive.WriteUInt32(payload, 0, (uint)payload.Length + 1);
        using var archive = OpenCompressed(payload, 0);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("stored-size prefix", exception.Message);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void OpenDecompressed_UnsupportedCompressionHeaderThrows(int offset)
    {
        var payload = CreatePayload([0xFC], 0);
        payload[offset] ^= 0xFF;
        using var archive = OpenCompressed(payload, 0);

        var exception = Assert.Throws<NotSupportedException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("10 FB", exception.Message);
    }

    [Fact]
    public void OpenDecompressed_HeaderAndDirectorySizeMismatchThrows()
    {
        var payload = CreatePayload([0xFC], 0);
        using var archive = OpenCompressed(payload, 1);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("compression directory declares 1", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    public void OpenDecompressed_TruncatedHeaderThrows(int length)
    {
        var payload = CreatePayload([0xFC], 0)[..length];
        using var archive = OpenCompressed(payload, 0);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("header is truncated", exception.Message);
    }

    [Fact]
    public void OpenDecompressed_TruncatedCommandsAndLiteralsThrow()
    {
        var commands = new byte[][]
        {
            [0x00],
            [0x80, 0x00],
            [0xC0, 0x00, 0x00],
            [0xE0, 0x41, 0x42],
            [0xFF, 0x41, 0x42],
        };

        foreach (var command in commands)
        {
            var payload = CreatePayload(command, 128);
            using var archive = OpenCompressed(payload, 128);
            var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
            Assert.Contains("truncated", exception.Message);
        }
    }

    [Fact]
    public void OpenDecompressed_BackReferenceBeforeOutputThrows()
    {
        var payload = CreatePayload([0x00, 0x00, 0xFC], 3);
        using var archive = OpenCompressed(payload, 3);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("only 0 bytes available", exception.Message);
    }

    [Fact]
    public void OpenDecompressed_OutputOverrunThrows()
    {
        var payload = CreatePayload([0xE0, 1, 2, 3, 4, 0xFC], 3);
        using var archive = OpenCompressed(payload, 3);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("exceeds the declared output size", exception.Message);
    }

    [Fact]
    public void OpenDecompressed_OutputSizeUnderrunThrows()
    {
        var payload = CreatePayload([0xFF, 1, 2, 3], 4);
        using var archive = OpenCompressed(payload, 4);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("final command produced 3 bytes", exception.Message);
    }

    [Fact]
    public void OpenDecompressed_MissingFinalCommandThrows()
    {
        var payload = CreatePayload([0xE0, 1, 2, 3, 4], 4);
        using var archive = OpenCompressed(payload, 4);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("without a final command", exception.Message);
    }

    [Fact]
    public void OpenDecompressed_TrailingBytesThrow()
    {
        var payload = CreatePayload([0xFC, 0x00], 0);
        using var archive = OpenCompressed(payload, 0);

        var exception = Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));
        Assert.Contains("trailing bytes", exception.Message);
    }

    private static DbpfArchive OpenCompressed(byte[] payload, uint uncompressedSize, byte[]? directory = null)
    {
        directory ??= CreateDirectory((ResourceTgi, uncompressedSize));
        var bytes = TestArchive.Create((ResourceTgi, payload), (DirectoryTgi, directory));
        return DbpfArchive.Open(new MemoryStream(bytes));
    }

    private static byte[] CreatePayload(byte[] commands, int uncompressedSize)
    {
        var payload = new byte[9 + commands.Length];
        TestArchive.WriteUInt32(payload, 0, (uint)payload.Length);
        payload[4] = 0x10;
        payload[5] = 0xFB;
        payload[6] = (byte)(uncompressedSize >> 16);
        payload[7] = (byte)(uncompressedSize >> 8);
        payload[8] = (byte)uncompressedSize;
        commands.CopyTo(payload, 9);
        return payload;
    }

    private static byte[] CreateDirectory(params (Tgi Identifier, uint Size)[] entries)
    {
        var directory = new byte[entries.Length * 16];
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = index * 16;
            TestArchive.WriteUInt32(directory, offset, entries[index].Identifier.Type);
            TestArchive.WriteUInt32(directory, offset + 4, entries[index].Identifier.Group);
            TestArchive.WriteUInt32(directory, offset + 8, entries[index].Identifier.Instance);
            TestArchive.WriteUInt32(directory, offset + 12, entries[index].Size);
        }

        return directory;
    }

    [Fact]
    public void OpenDecompressed_InvalidHeaderDoesNotReadWholeStoredResource()
    {
        var payload = CreatePayload([0xFC], 0);
        payload[4] = 0x11;
        var directory = CreateDirectory((ResourceTgi, 0));
        var bytes = TestArchive.Create((ResourceTgi, payload), (DirectoryTgi, directory));
        const uint storedSize = 64 * 1024 * 1024;
        TestArchive.WriteUInt32(bytes, TestArchive.HeaderSize, storedSize);
        var indexOffset = TestArchive.HeaderSize + payload.Length + directory.Length;
        TestArchive.WriteUInt32(bytes, indexOffset + 16, storedSize);
        using var stream = new VirtualLengthStream(bytes, TestArchive.HeaderSize + (long)storedSize);
        using var archive = DbpfArchive.Open(stream, leaveOpen: true);

        Assert.Throws<NotSupportedException>(() => archive.OpenDecompressed(ResourceTgi));
    }

    [Fact]
    public void OpenDecompressed_InvalidCommandsDoNotAllocateDeclaredOutput()
    {
        const int declaredSize = 0xFFFFFF;
        var payload = CreatePayload([0xFC], declaredSize);
        using var archive = OpenCompressed(payload, declaredSize);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<InvalidDataException>(() => archive.OpenDecompressed(ResourceTgi));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.True(allocated < 1_000_000, $"The failing operation allocated {allocated} bytes.");
    }

    private sealed class VirtualLengthStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly long _length;
        private long _position;

        internal VirtualLengthStream(byte[] bytes, long length)
        {
            _bytes = bytes;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
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
            if (_position > _bytes.Length || buffer.Length > _bytes.Length - _position)
                throw new EndOfStreamException("The read exceeded the available test data.");

            _bytes.AsSpan((int)_position, buffer.Length).CopyTo(buffer);
            _position += buffer.Length;
            return buffer.Length;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            return Position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
