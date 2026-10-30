# Sakuno.DBPF

Sakuno.DBPF is a clean-room C#/.NET reader for SimCity 4 Database Packed Files (DBPF).

## Supported functionality

- DBPF 1.0 headers and index 7.0, as verified with local SimCity 4 data files.
- TGI resource identifiers and regular-index metadata.
- On-demand, bounded access to stored resource bytes.
- Explicit errors for malformed input and unsupported container versions.

The library does not currently decompress resources, parse specific resource formats, or modify archives. Unknown resource types remain accessible as raw bytes. See the [container format specification](docs/specs/dbpf-container.md) for details.

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
using var resource = archive.OpenRead(identifier);
using var output = File.Create("resource.bin");
resource.CopyTo(output);
```

`OpenRead` returns the bytes stored in the archive without decompression. Pass `leaveOpen: true` to `DbpfArchive.Open` when the caller needs to reuse the input stream after disposing the archive.
