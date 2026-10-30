using Sakuno.DBPF.Cli;

namespace Sakuno.DBPF.Tests;

public sealed class CliTests : IDisposable
{
    private static readonly Tgi ResourceTgi = new(0x11223344, 0x55667788, 0x99AABBCC);
    private static readonly Tgi OtherTgi = new(0x12345678, 0x23456789, 0x3456789A);
    private static readonly Tgi DirectoryTgi = new(0xE86B1EEF, 0xE86B1EEF, 0x286B1F03);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Sakuno.DBPF.Tests-{Guid.NewGuid():N}");

    public CliTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void InfoAndList_ReadGeneratedArchive()
    {
        var path = WriteArchive("archive.dat", TestArchive.Create(
            (ResourceTgi, [1, 2, 3]),
            (OtherTgi, [4, 5])));

        var info = Run("info", path);
        var list = Run("list", path, "--offset", "1", "--limit", "1");

        Assert.Equal(0, info.ExitCode);
        Assert.Contains("Archive version: 1.0", info.Output);
        Assert.Contains("Index entries: 2", info.Output);
        Assert.Contains("Compressed resources: 0", info.Output);
        Assert.Empty(info.Error);
        Assert.Equal(0, list.ExitCode);
        Assert.Contains(OtherTgi.ToString(), list.Output);
        Assert.DoesNotContain(ResourceTgi.ToString(), list.Output);
        Assert.Contains("Entries: 1 of 2 (offset 1).", list.Output);
    }

    [Fact]
    public void InspectAndCheck_ReadCompressedResources()
    {
        var path = WriteCompressedArchive(out _);

        var inspect = Run("inspect", path, ResourceTgi.ToString());
        var check = Run("check", path);

        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("Compressed: True", inspect.Output);
        Assert.Contains("Raw read: OK (15 bytes)", inspect.Output);
        Assert.Contains("Decompressed read: OK (4 bytes)", inspect.Output);
        Assert.Equal(0, check.ExitCode);
        Assert.Contains("Validated 2 resources (1 compressed), 20 output bytes.", check.Output);
    }

    [Fact]
    public void Extract_WritesDecompressedAndRawBytes()
    {
        var path = WriteCompressedArchive(out var payload);
        var decompressedPath = Path.Combine(_directory, "decompressed.bin");
        var rawPath = Path.Combine(_directory, "raw.bin");

        var decompressed = Run("extract", path, ResourceTgi.ToString(), decompressedPath);
        var raw = Run("extract", path, ResourceTgi.ToString(), rawPath, "--raw");

        Assert.Equal(0, decompressed.ExitCode);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(decompressedPath));
        Assert.Equal(0, raw.ExitCode);
        Assert.Equal(payload, File.ReadAllBytes(rawPath));
    }

    [Fact]
    public void Extract_ExistingFileRequiresOverwriteOption()
    {
        var path = WriteCompressedArchive(out _);
        var outputPath = Path.Combine(_directory, "resource.bin");
        File.WriteAllText(outputPath, "original");

        var rejected = Run("extract", path, ResourceTgi.ToString(), outputPath);

        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("Error:", rejected.Error);
        Assert.Equal("original", File.ReadAllText(outputPath));

        var overwritten = Run("extract", path, ResourceTgi.ToString(), outputPath, "--overwrite");
        Assert.Equal(0, overwritten.ExitCode);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public void Extract_RejectsInputArchiveAsDestination()
    {
        var path = WriteCompressedArchive(out _);
        var original = File.ReadAllBytes(path);

        var result = Run("extract", path, ResourceTgi.ToString(), path, "--overwrite");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("destination must differ", result.Error);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void CommandErrors_ReturnFailureAndClearMessage()
    {
        var path = WriteArchive("archive.dat", TestArchive.Create((ResourceTgi, [1])));

        var invalidTgi = Run("inspect", path, "invalid");
        var missingFile = Run("info", Path.Combine(_directory, "missing.dat"));
        var invalidLimit = Run("list", path, "--limit", "0");

        Assert.Equal(1, invalidTgi.ExitCode);
        Assert.Contains("TGI must use", invalidTgi.Error);
        Assert.Equal(1, missingFile.ExitCode);
        Assert.Contains("Error:", missingFile.Error);
        Assert.Equal(1, invalidLimit.ExitCode);
        Assert.Contains("limit must be greater than zero", invalidLimit.Error);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteCompressedArchive(out byte[] payload)
    {
        payload = new byte[15];
        TestArchive.WriteUInt32(payload, 0, (uint)payload.Length);
        payload[4] = 0x10;
        payload[5] = 0xFB;
        payload[8] = 4;
        payload[9] = 0xE0;
        payload[10] = 1;
        payload[11] = 2;
        payload[12] = 3;
        payload[13] = 4;
        payload[14] = 0xFC;

        var directory = new byte[16];
        TestArchive.WriteUInt32(directory, 0, ResourceTgi.Type);
        TestArchive.WriteUInt32(directory, 4, ResourceTgi.Group);
        TestArchive.WriteUInt32(directory, 8, ResourceTgi.Instance);
        TestArchive.WriteUInt32(directory, 12, 4);
        return WriteArchive("compressed.dat", TestArchive.Create((ResourceTgi, payload), (DirectoryTgi, directory)));
    }

    private string WriteArchive(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = CliApplication.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }
}
