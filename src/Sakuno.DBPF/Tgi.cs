namespace Sakuno.DBPF;

/// <summary>Identifies a resource by its type, group, and instance IDs.</summary>
public readonly record struct Tgi(uint Type, uint Group, uint Instance)
{
    public override string ToString() => $"{Type:X8}-{Group:X8}-{Instance:X8}";
}
