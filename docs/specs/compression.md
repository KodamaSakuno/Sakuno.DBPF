# SimCity 4 compressed resources

## Status

- **Verified:** DBDF compression directories and the SimCity 4 RefPack envelope used by the locally available data files.
- **Implemented:** Compression metadata, raw stored-byte access, and decompression through the public archive API.
- **Planned:** Other RefPack header variants and compression algorithms.

Container fields and TGI identifiers are defined in [the container specification](dbpf-container.md).

## Compression directory

A DBPF archive can contain a compression directory resource with this fixed TGI:

| Field | Value |
| --- | --- |
| Type | `0xE86B1EEF` |
| Group | `0xE86B1EEF` |
| Instance | `0x286B1F03` |

The directory payload is a sequence of 16-byte records. All fields are unsigned 32-bit little-endian values.

| Record offset | Size | Field |
| ---: | ---: | --- |
| 0x00 | 4 | Compressed resource Type ID |
| 0x04 | 4 | Compressed resource Group ID |
| 0x08 | 4 | Compressed resource Instance ID |
| 0x0C | 4 | Decompressed resource size |

The directory identifies which regular-index entries are compressed. Its records do not replace the regular index and do not contain stored offsets or stored sizes. The reader rejects a partial record, a duplicate resource record, a record for a missing resource, and a record for the directory itself.

An archive without this directory has no resources marked as compressed. Resource bytes that happen to start with a compression signature are not interpreted unless a directory record marks the resource.

## SimCity 4 RefPack envelope

Each compressed resource observed in the supported SimCity 4 files has a nine-byte prefix before its commands:

| Offset | Size | Encoding | Field |
| ---: | ---: | --- | --- |
| 0x00 | 4 | Little-endian | Total stored resource size, including these nine bytes |
| 0x04 | 1 | Exact value | Flags byte `0x10` |
| 0x05 | 1 | Exact value | Signature byte `0xFB` |
| 0x06 | 3 | Big-endian | Decompressed size |

The size at offset zero must equal the regular-index entry's stored size. The size at offset six must equal both the DBDF record's decompressed size and the number of bytes produced by the commands. Other flags or signatures are rejected as unsupported rather than decoded using an assumed layout.

## Commands

Commands begin at offset `0x09`. The first command byte is `b0`; subsequent command bytes are `b1`, `b2`, and `b3` when required. Literal bytes follow the command bytes and are copied directly to the output. A back-reference then copies already produced bytes, unless the command is a literal-only or final command.

| First byte | Command bytes | Literal count | Match length | Back-reference distance |
| --- | ---: | ---: | ---: | ---: |
| `0x00`-`0x7F` | 2 | `b0 & 3` | `((b0 >> 2) & 7) + 3` | `((b0 & 0x60) << 3) + b1 + 1` |
| `0x80`-`0xBF` | 3 | `(b1 >> 6) & 3` | `(b0 & 0x3F) + 4` | `((b1 & 0x3F) << 8) + b2 + 1` |
| `0xC0`-`0xDF` | 4 | `b0 & 3` | `((b0 & 0x0C) << 6) + b3 + 5` | `((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1` |
| `0xE0`-`0xFB` | 1 | `((b0 & 0x1F) << 2) + 4` | None | None |
| `0xFC`-`0xFF` | 1 | `b0 & 3` | None; ends the stream | None |

Back-references may overlap their output. Bytes are therefore copied forward one at a time. A distance of one repeats the last produced byte.

The reader rejects truncated commands or literals, output overruns, back-references before the beginning of the output, a missing final command, trailing bytes after the final command, and a final output size different from either declared size. Allocation is limited to one resource's declared decompressed size; sizes that cannot be represented by the managed array implementation are rejected.

## Sources and license status

- [OpenReliant: RefPack compression](https://github.com/OpenReliant/openreliant/blob/main/docs/formats/refpack.md), consulted for the generic RefPack header and command structure. The repository is MPL-2.0 licensed. No implementation code was consulted or copied.
- [SC4D Encyclopaedia: DBDF](https://wiki.sc4devotion.com/index.php?title=DBDF), search-result format facts used to locate the directory layout. No license statement was available; no code, examples, or expressive text were copied.
- Independent read-only observation of all locally available SimCity 4 data files, used to confirm the four-byte stored-size prefix, RefPack signature, decompressed sizes, and DBDF relationships. Proprietary file contents are not included in this repository.

The decompression implementation is original and uses only the structural facts documented here.
