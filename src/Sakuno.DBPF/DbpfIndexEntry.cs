namespace Sakuno.DBPF;

/// <summary>Locates the stored bytes of a resource in a DBPF archive.</summary>
public sealed record DbpfIndexEntry(Tgi Identifier, uint Offset, uint Size);
