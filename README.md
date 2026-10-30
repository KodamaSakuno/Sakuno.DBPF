# Sakuno.DBPF

Sakuno.DBPF is a clean-room C#/.NET reader for SimCity 4 Database Packed Files (DBPF).

## Supported functionality

- DBPF 1.0 headers and index 7.0, as verified with local SimCity 4 data files.
- TGI resource identifiers and regular-index metadata.
- On-demand, bounded access to stored resource bytes.
- DBDF compression metadata and SimCity 4 RefPack decompression.
- Explicit errors for malformed input and unsupported container or compression versions.

The library does not currently parse specific resource formats or modify archives. Unknown resource types remain accessible as raw bytes. See the [container format specification](docs/specs/dbpf-container.md) and [compression specification](docs/specs/compression.md) for details.

## Requirements

Use a .NET SDK that supports .NET 10. The repository does not pin an SDK version.

## Build and test

```bash
dotnet build Sakuno.DBPF.slnx
dotnet test Sakuno.DBPF.slnx
```

The tests use generated binary samples and do not require local game files.

## Basic usage

```csharp
using Sakuno.DBPF;

using var file = File.OpenRead("SimCity_1.dat");
using var archive = DbpfArchive.Open(file);

foreach (var entry in archive.Entries)
    Console.WriteLine($"{entry.Identifier}: {entry.Size} bytes at {entry.Offset}");

var identifier = new Tgi(0x11223344, 0x55667788, 0x99AABBCC);
using var resource = archive.OpenDecompressed(identifier);
using var output = File.Create("resource.bin");
resource.CopyTo(output);
```

`OpenDecompressed` decompresses resources listed in the DBDF directory and returns stored bytes directly for other resources. Use `OpenRead` when the original stored bytes are required.

`DbpfArchive.Open` takes ownership of the input stream by default, including when opening fails. Pass `leaveOpen: true` when the caller retains ownership or needs to reuse the stream after disposing the archive.
