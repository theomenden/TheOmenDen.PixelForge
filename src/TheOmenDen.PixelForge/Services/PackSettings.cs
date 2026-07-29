namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// The three pack directories as persisted. Plain strings rather than <c>FullPath</c>: the
/// source-generated serialiser needs no converter for a string, and the conversion belongs at
/// the service boundary where a path can also be validated.
/// </summary>
public sealed record PackSettings
{
    public string? CoreAssets { get; init; }

    public string? Expansion1Assets { get; init; }

    public string? Expansion2Assets { get; init; }
}
