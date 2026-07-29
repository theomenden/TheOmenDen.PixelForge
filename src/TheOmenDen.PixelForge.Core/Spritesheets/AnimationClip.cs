namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// One animation carved out of a Time Elements source sheet, addressed by the column its
/// first frame occupies. Frames run left to right from there, on every facing row.
/// </summary>
public readonly record struct AnimationClip
{
    public required string Name { get; init; }

    /// <summary>Column of frame 0 within the 23-column source sheet.</summary>
    public required int SourceColumn { get; init; }

    public required int FrameCount { get; init; }

    /// <summary>Column just past the clip's last frame.</summary>
    public int SourceColumnEnd => SourceColumn + FrameCount;
}
