# DBPF container format

## Status

- **Verified:** DBPF 1.0 with index 7.0, as used by the locally available SimCity 4 data files.
- **Implemented:** Header validation, regular-index reading, and bounded reads of raw resource bytes.
- **Planned:** Other container versions, hole-table interpretation, and resource-specific parsing. Compression is documented separately in [the compression specification](compression.md).

All integer fields described here are unsigned 32-bit little-endian values unless stated otherwise. Offsets are relative to the beginning of the archive and lengths are measured in bytes.

## Header

The DBPF 1.0 header occupies 96 bytes.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0x00 | 4 | ASCII magic `DBPF` |
| 0x04 | 4 | Archive major version; `1` |
| 0x08 | 4 | Archive minor version; `0` |
| 0x0C | 12 | Reserved; contents are not interpreted |
| 0x18 | 4 | Creation time as a Unix timestamp |
| 0x1C | 4 | Modification time as a Unix timestamp |
| 0x20 | 4 | Index major version; `7` |
| 0x24 | 4 | Regular-index entry count |
| 0x28 | 4 | Regular-index offset |
| 0x2C | 4 | Regular-index byte length |
| 0x30 | 4 | Hole-table entry count |
| 0x34 | 4 | Hole-table offset |
| 0x38 | 4 | Hole-table byte length |
| 0x3C | 4 | Index minor version; `0` |
| 0x40 | 32 | Reserved; contents are not interpreted |

The reader rejects other archive or index versions instead of assuming that their layouts are compatible. Reserved fields are preserved neither in the public metadata model nor when reading resources; the reader does not require them to be zero.

## Regular index (version 7.0)

The regular index is a contiguous sequence of 20-byte entries. Its declared byte length must equal the entry count multiplied by 20.

| Entry offset | Size | Field |
| ---: | ---: | --- |
| 0x00 | 4 | Type ID |
| 0x04 | 4 | Group ID |
| 0x08 | 4 | Instance ID |
| 0x0C | 4 | Resource data offset |
| 0x10 | 4 | Resource data byte length |

The Type, Group, and Instance IDs form the resource's TGI identifier. Index version 7.0 has no second instance field.

An index entry describes the stored bytes exactly. A nonzero resource length does not imply that the resource is uncompressed or decoded. Compression is handled separately from container reading.

## Hole table

The header can locate a hole table. For the supported index layout, the reader validates that its byte length equals its entry count multiplied by 20 and that the table lies within the archive. Hole entries are not exposed or otherwise interpreted. An empty hole table may have an offset of zero.

## Bounds and malformed input

The reader requires a readable, seekable stream of at least 96 bytes. It rejects:

- a missing or truncated header;
- an invalid magic value;
- unsupported archive or index versions;
- an index or hole table whose declared length does not match its entry count;
- an index, hole table, or resource range outside the stream;
- duplicate TGI identifiers in the regular index.

Range checks use widened arithmetic. A resource stream is bounded by its indexed length and cannot read adjacent archive data.

## Sources and license status

- [Niotso Wiki: DBPF](http://wiki.niotso.org/DBPF), consulted for format facts. No license statement was available on the consulted page; no code, pseudocode, examples, or expressive text were copied.
- Independent read-only observation of locally available SimCity 4 data files, used to confirm the DBPF 1.0 / index 7.0 field layout and 20-byte entries. Proprietary file contents are not included in this repository.

The implementation is original and uses only the structural facts documented here.
