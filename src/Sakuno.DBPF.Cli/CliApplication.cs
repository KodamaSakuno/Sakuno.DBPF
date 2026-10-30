using System.CommandLine;

namespace Sakuno.DBPF.Cli;

public static class CliApplication
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var root = new RootCommand("Inspect and extract resources from SimCity 4 DBPF archives.");
        root.Subcommands.Add(CreateInfoCommand(output, error));
        root.Subcommands.Add(CreateListCommand(output, error));
        root.Subcommands.Add(CreateInspectCommand(output, error));
        root.Subcommands.Add(CreateCheckCommand(output, error));
        root.Subcommands.Add(CreateExtractCommand(output, error));
        return root.Parse(args).Invoke();
    }

    private static Command CreateInfoCommand(TextWriter output, TextWriter error)
    {
        var path = new Argument<string>("path") { Description = "Path to the DBPF archive." };
        var command = new Command("info", "Show archive and compression metadata.");
        command.Arguments.Add(path);
        command.SetAction(result => Execute(error, () =>
        {
            using var archive = OpenArchive(result.GetRequiredValue(path));
            var compressedCount = archive.Entries.Count(entry => entry.IsCompressed);
            var storedBytes = archive.Entries.Sum(entry => (long)entry.Size);
            var decompressedBytes = archive.Entries.Sum(entry => (long)(entry.UncompressedSize ?? entry.Size));

            output.WriteLine($"Archive version: {archive.Header.MajorVersion}.{archive.Header.MinorVersion}");
            output.WriteLine($"Index version: {archive.Header.IndexMajorVersion}.{archive.Header.IndexMinorVersion}");
            output.WriteLine($"Index entries: {archive.Entries.Count}");
            output.WriteLine($"Compressed resources: {compressedCount}");
            output.WriteLine($"Stored resource bytes: {storedBytes}");
            output.WriteLine($"Resource bytes after decompression: {decompressedBytes}");
        }));
        return command;
    }

    private static Command CreateListCommand(TextWriter output, TextWriter error)
    {
        var path = new Argument<string>("path") { Description = "Path to the DBPF archive." };
        var offset = new Option<int>("--offset")
        {
            Description = "Number of index entries to skip.",
            DefaultValueFactory = _ => 0,
        };
        var limit = new Option<int>("--limit")
        {
            Description = "Maximum number of entries to display.",
            DefaultValueFactory = _ => 100,
        };
        var command = new Command("list", "List regular-index entries.");
        command.Arguments.Add(path);
        command.Options.Add(offset);
        command.Options.Add(limit);
        command.SetAction(result => Execute(error, () =>
        {
            var offsetValue = result.GetValue(offset);
            var limitValue = result.GetValue(limit);
            if (offsetValue < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "The offset cannot be negative.");
            if (limitValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(limit), "The limit must be greater than zero.");

            using var archive = OpenArchive(result.GetRequiredValue(path));
            var start = Math.Min(offsetValue, archive.Entries.Count);
            var count = Math.Min(limitValue, archive.Entries.Count - start);
            for (var index = start; index < start + count; index++)
            {
                var entry = archive.Entries[index];
                output.Write(entry.Identifier);
                output.Write($" Offset={entry.Offset} Size={entry.Size}");
                if (entry.UncompressedSize is { } uncompressedSize)
                    output.Write($" UncompressedSize={uncompressedSize}");
                output.WriteLine();
            }

            output.WriteLine($"Entries: {count} of {archive.Entries.Count} (offset {start}).");
        }));
        return command;
    }

    private static Command CreateInspectCommand(TextWriter output, TextWriter error)
    {
        var path = new Argument<string>("path") { Description = "Path to the DBPF archive." };
        var tgi = new Argument<string>("tgi") { Description = "Resource TGI in TTTTTTTT-GGGGGGGG-IIIIIIII format." };
        var command = new Command("inspect", "Read and validate one resource.");
        command.Arguments.Add(path);
        command.Arguments.Add(tgi);
        command.SetAction(result => Execute(error, () =>
        {
            using var archive = OpenArchive(result.GetRequiredValue(path));
            var identifier = TgiParser.Parse(result.GetRequiredValue(tgi));
            var entry = archive.GetEntry(identifier);

            output.WriteLine($"TGI: {entry.Identifier}");
            output.WriteLine($"Offset: {entry.Offset}");
            output.WriteLine($"Stored size: {entry.Size}");
            output.WriteLine($"Compressed: {entry.IsCompressed}");
            if (entry.UncompressedSize is { } uncompressedSize)
                output.WriteLine($"Uncompressed size: {uncompressedSize}");

            using (var raw = archive.OpenRead(identifier))
                output.WriteLine($"Raw read: OK ({Drain(raw)} bytes)");
            using (var resource = archive.OpenDecompressed(identifier))
                output.WriteLine($"Decompressed read: OK ({Drain(resource)} bytes)");
        }));
        return command;
    }

    private static Command CreateCheckCommand(TextWriter output, TextWriter error)
    {
        var path = new Argument<string>("path") { Description = "Path to the DBPF archive." };
        var command = new Command("check", "Read every resource and validate decompressed lengths.");
        command.Arguments.Add(path);
        command.SetAction(result => Execute(error, () =>
        {
            using var archive = OpenArchive(result.GetRequiredValue(path));
            var compressedCount = 0;
            var totalBytes = 0L;
            foreach (var entry in archive.Entries)
            {
                using var resource = archive.OpenDecompressed(entry.Identifier);
                var bytesRead = Drain(resource);
                var expectedSize = entry.UncompressedSize ?? entry.Size;
                if (bytesRead != expectedSize)
                {
                    throw new InvalidDataException(
                        $"Resource {entry.Identifier} produced {bytesRead} bytes; expected {expectedSize}.");
                }

                if (entry.IsCompressed)
                    compressedCount++;
                totalBytes += bytesRead;
            }

            output.WriteLine($"Validated {archive.Entries.Count} resources ({compressedCount} compressed), {totalBytes} output bytes.");
        }));
        return command;
    }

    private static Command CreateExtractCommand(TextWriter output, TextWriter error)
    {
        var path = new Argument<string>("path") { Description = "Path to the DBPF archive." };
        var tgi = new Argument<string>("tgi") { Description = "Resource TGI in TTTTTTTT-GGGGGGGG-IIIIIIII format." };
        var destination = new Argument<string>("output") { Description = "Destination file path." };
        var raw = new Option<bool>("--raw") { Description = "Extract stored bytes without decompression." };
        var overwrite = new Option<bool>("--overwrite") { Description = "Overwrite an existing destination file." };
        var command = new Command("extract", "Extract one resource.");
        command.Arguments.Add(path);
        command.Arguments.Add(tgi);
        command.Arguments.Add(destination);
        command.Options.Add(raw);
        command.Options.Add(overwrite);
        command.SetAction(result => Execute(error, () =>
        {
            var archivePath = result.GetRequiredValue(path);
            var outputPath = result.GetRequiredValue(destination);
            if (string.Equals(Path.GetFullPath(archivePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The destination must differ from the input archive.", nameof(destination));

            using var archive = OpenArchive(archivePath);
            var identifier = TgiParser.Parse(result.GetRequiredValue(tgi));
            using var resource = result.GetValue(raw) ? archive.OpenRead(identifier) : archive.OpenDecompressed(identifier);
            var mode = result.GetValue(overwrite) ? FileMode.Create : FileMode.CreateNew;
            using var outputFile = new FileStream(outputPath, mode, FileAccess.Write, FileShare.None);
            resource.CopyTo(outputFile);
            output.WriteLine($"Extracted {resource.Position} bytes to {outputPath}.");
        }));
        return command;
    }

    private static DbpfArchive OpenArchive(string path) => DbpfArchive.Open(File.OpenRead(path));

    private static long Drain(Stream stream)
    {
        stream.CopyTo(Stream.Null);
        return stream.Position;
    }

    private static int Execute(TextWriter error, Action action)
    {
        try
        {
            action();
            return 0;
        }
        catch (Exception exception) when (exception is IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or KeyNotFoundException
            or FormatException
            or ArgumentException)
        {
            error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
