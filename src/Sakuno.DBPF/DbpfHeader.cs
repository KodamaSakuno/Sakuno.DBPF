namespace Sakuno.DBPF;

/// <summary>Contains the metadata stored in a DBPF 1.0 header.</summary>
public sealed record DbpfHeader(
    uint MajorVersion,
    uint MinorVersion,
    uint CreatedTimestamp,
    uint ModifiedTimestamp,
    uint IndexMajorVersion,
    uint IndexMinorVersion,
    uint IndexEntryCount,
    uint IndexOffset,
    uint IndexSize,
    uint HoleEntryCount,
    uint HoleOffset,
    uint HoleSize);
