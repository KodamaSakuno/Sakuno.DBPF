namespace Sakuno.DBPF.Tests;

public sealed class DbpfArchiveTests
{
    private static readonly Tgi FirstTgi = new(0x11223344, 0x55667788, 0x99AABBCC);
    private static readonly Tgi SecondTgi = new(0x12345678, 0x23456789, 0x3456789A);
    private static readonly byte[] FirstData = [0x00, 0x7F, 0x80, 0xFF];
    private static readonly byte[] SecondData = [0xDE, 0xAD, 0xBE, 0xEF, 0x42];

    [Fact]
    public void Open_ReadsHeaderAndIndex()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData), (SecondTgi, SecondData));

        using var archive = Open(bytes);

        Assert.Equal(1u, archive.Header.MajorVersion);
        Assert.Equal(0u, archive.Header.MinorVersion);
        Assert.Equal(0x01020304u, archive.Header.CreatedTimestamp);
        Assert.Equal(0x05060708u, archive.Header.ModifiedTimestamp);
        Assert.Equal(7u, archive.Header.IndexMajorVersion);
        Assert.Equal(0u, archive.Header.IndexMinorVersion);
        Assert.Equal(2u, archive.Header.IndexEntryCount);
        Assert.Equal(2, archive.Entries.Count);

        var first = archive.GetEntry(FirstTgi);
        Assert.Equal(FirstTgi, first.Identifier);
        Assert.Equal(96u, first.Offset);
        Assert.Equal((uint)FirstData.Length, first.Size);
        Assert.Same(first, archive.Entries[0]);

        var second = archive.GetEntry(SecondTgi);
        Assert.Equal(SecondTgi, second.Identifier);
        Assert.Equal(100u, second.Offset);
        Assert.Equal((uint)SecondData.Length, second.Size);
        Assert.Same(second, archive.Entries[1]);
    }

    [Fact]
    public void OpenRead_ReturnsBoundedRawResourceBytes()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData), (SecondTgi, SecondData));
        using var archive = Open(bytes);

        using var resource = archive.OpenRead(SecondTgi);
        var result = new byte[SecondData.Length + 3];
        var bytesRead = resource.Read(result);

        Assert.Equal(SecondData.Length, bytesRead);
        Assert.Equal(SecondData, result[..SecondData.Length]);
        Assert.Equal(-1, resource.ReadByte());

        Assert.Equal(4, resource.Seek(-1, SeekOrigin.End));
        Assert.Equal(0x42, resource.ReadByte());
        Assert.Throws<IOException>(() => resource.Seek(1, SeekOrigin.End));
    }

    [Fact]
    public void OpenRead_ReadingOneResourceDoesNotChangeAnotherResourcePosition()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData), (SecondTgi, SecondData));
        using var archive = Open(bytes);
        using var first = archive.OpenRead(FirstTgi);
        using var second = archive.OpenRead(SecondTgi);

        Assert.Equal(FirstData[0], first.ReadByte());
        Assert.Equal(SecondData[0], second.ReadByte());
        Assert.Equal(FirstData[1], first.ReadByte());
        Assert.Equal(SecondData[1], second.ReadByte());
    }

    [Fact]
    public void Open_DoesNotReadResourceBytesUntilRequested()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData));
        using var stream = new TrackingStream(bytes);

        using var archive = DbpfArchive.Open(stream);

        Assert.Equal(TestArchive.HeaderSize + TestArchive.IndexEntrySize, stream.BytesRead);
        using var resource = archive.OpenRead(FirstTgi);
        Assert.Equal(TestArchive.HeaderSize + TestArchive.IndexEntrySize, stream.BytesRead);
        resource.ReadByte();
        Assert.Equal(TestArchive.HeaderSize + TestArchive.IndexEntrySize + 1, stream.BytesRead);
    }

    [Fact]
    public void Open_AcceptsEmptyArchiveAndEmptyResource()
    {
        using var emptyArchive = Open(TestArchive.Create());
        Assert.Empty(emptyArchive.Entries);

        using var archive = Open(TestArchive.Create((FirstTgi, [])));
        using var resource = archive.OpenRead(FirstTgi);
        Assert.Equal(0, resource.Length);
        Assert.Equal(-1, resource.ReadByte());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispose_HonorsLeaveOpen(bool leaveOpen)
    {
        var stream = new MemoryStream(TestArchive.Create());
        var archive = DbpfArchive.Open(stream, leaveOpen);

        archive.Dispose();

        Assert.Equal(leaveOpen, stream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => archive.GetEntry(FirstTgi));
    }

    [Fact]
    public void GetEntry_UnknownIdentifierThrows()
    {
        using var archive = Open(TestArchive.Create((FirstTgi, FirstData)));

        var exception = Assert.Throws<KeyNotFoundException>(() => archive.GetEntry(SecondTgi));
        Assert.Contains(SecondTgi.ToString(), exception.Message);
    }

    [Fact]
    public void Open_InvalidMagicThrows()
    {
        var bytes = TestArchive.Create();
        "NOPE"u8.CopyTo(bytes);

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("magic", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(95)]
    public void Open_TruncatedHeaderThrows(int length)
    {
        var bytes = TestArchive.Create()[..length];

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("header", exception.Message);
    }

    [Theory]
    [InlineData(0x04, 2u)]
    [InlineData(0x08, 1u)]
    [InlineData(0x20, 8u)]
    [InlineData(0x3C, 1u)]
    public void Open_UnsupportedVersionThrows(int offset, uint value)
    {
        var bytes = TestArchive.Create();
        TestArchive.WriteUInt32(bytes, offset, value);

        Assert.Throws<NotSupportedException>(() => Open(bytes));
    }

    [Fact]
    public void Open_IndexSizeMismatchThrows()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData));
        TestArchive.WriteUInt32(bytes, 0x2C, 19);

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("regular index", exception.Message);
        Assert.Contains("20", exception.Message);
    }

    [Fact]
    public void Open_IndexOutsideStreamThrows()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData));
        TestArchive.WriteUInt32(bytes, 0x28, uint.MaxValue);

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("regular index", exception.Message);
        Assert.Contains("stream length", exception.Message);
    }

    [Fact]
    public void Open_TruncatedIndexThrows()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData))[..^1];

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("regular index", exception.Message);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    public void Open_ResourceOutsideStreamThrows(int entryFieldOffset)
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData));
        TestArchive.WriteUInt32(bytes, TestArchive.HeaderSize + FirstData.Length + entryFieldOffset, uint.MaxValue);

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("resource", exception.Message);
        Assert.Contains("entry 0", exception.Message);
    }

    [Fact]
    public void Open_DuplicateIdentifierThrows()
    {
        var bytes = TestArchive.Create((FirstTgi, FirstData), (FirstTgi, SecondData));

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("duplicate TGI", exception.Message);
        Assert.Contains(FirstTgi.ToString(), exception.Message);
    }

    [Fact]
    public void Open_InvalidHoleTableThrows()
    {
        var bytes = TestArchive.Create();
        TestArchive.WriteUInt32(bytes, 0x30, 1);
        TestArchive.WriteUInt32(bytes, 0x34, 96);
        TestArchive.WriteUInt32(bytes, 0x38, 19);

        var exception = Assert.Throws<InvalidDataException>(() => Open(bytes));
        Assert.Contains("hole table", exception.Message);
    }

    private static DbpfArchive Open(byte[] bytes) => DbpfArchive.Open(new MemoryStream(bytes));

    private sealed class TrackingStream : MemoryStream
    {
        internal TrackingStream(byte[] bytes) : base(bytes)
        {
        }

        internal long BytesRead { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = base.Read(buffer);
            BytesRead += bytesRead;
            return bytesRead;
        }
    }
}
