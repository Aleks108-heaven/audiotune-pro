namespace AudioTunePro.Core.Models;

/// <summary>A named, saved EQ configuration — either built-in or user-created.</summary>
public sealed class Preset
{
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsBuiltIn { get; init; }
    public required EqEngine Engine { get; init; }
}
