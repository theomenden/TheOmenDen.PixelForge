namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>One row of <c>clips.csv</c>: a single frame of a single clip on a single facing.</summary>
/// <remarks>
/// Fully denormalised on purpose. A consumer reading this file should not have to know that
/// facings map onto source rows in a fixed order, or that a frame index is not the same thing as
/// a source column — both are stated per row.
/// </remarks>
public sealed record ClipIndexRow
{
    /// <summary>Snake-cased animation name, e.g. <c>nock_and_bow</c>.</summary>
    public required string Clip { get; init; }

    /// <summary>One of <see cref="GeneratorClips.Facings"/>.</summary>
    public required string Facing { get; init; }

    /// <summary>Row of the sheet this facing occupies, 0-3.</summary>
    public required int SourceRow { get; init; }

    /// <summary>Position within the clip's playback, from 0.</summary>
    public required int FrameIndex { get; init; }

    /// <summary>Column of the sheet to draw for this frame. Repeats where the animation does.</summary>
    public required int SourceColumn { get; init; }

    /// <summary>Cell edge in pixels.</summary>
    public required int CellSize { get; init; }

    /// <summary>How long this frame is held, in milliseconds.</summary>
    public required int FrameDurationMs { get; init; }

    /// <summary>
    /// Whether the generator composites this clip's layers back to front. <see langword="true"/>
    /// for <c>climb</c> alone — see <see cref="GeneratorClip.ReverseDrawOrder"/>.
    /// </summary>
    public required bool ReverseDrawOrder { get; init; }
}
