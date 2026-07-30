using System.Collections.Immutable;

namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// Geometry of the Time Elements source sheets and of the curated sheet Corvus consumes.
/// <para>
/// Source partials (<c>assets/&lt;slot&gt;/*.png</c>) are 1104x192 — 23 frames across,
/// 4 facing rows down, ordered south, west, east, north. The curated output keeps 8
/// animations on 3 facings and drops north entirely, because the wander model fixes
/// <c>y</c> per avatar so nothing ever walks away from the camera.
/// </para>
/// <para>
/// Column spans below are not inferred from the art — they are the generator's own
/// <c>Settings.json</c> <c>CharacterAnimations</c> block, which is why <c>jump</c> opens on
/// the crouch frame and <c>heavy_attack</c> opens on the wind-up frame.
/// </para>
/// </summary>
public static class SheetLayout
{
    public const int CellSize = 48;

    /// <summary>RGBA8888 — the canonical working format, four bytes per pixel.</summary>
    public const int BytesPerPixel = 4;

    public const int SourceColumns = 23;
    public const int SourceRows = 4;
    public const int SourceWidth = SourceColumns * CellSize;
    public const int SourceHeight = SourceRows * CellSize;

    /// <summary>South, west, east. North is source row 3 and is deliberately dropped.</summary>
    public const int FacingCount = 3;

    /// <summary><c>heavy_attack</c> is the widest clip at 5 frames, which sets the sheet width.</summary>
    public const int OutputColumns = 5;

    public const int OutputRows = ClipCount * FacingCount;
    public const int OutputWidth = OutputColumns * CellSize;
    public const int OutputHeight = OutputRows * CellSize;

    public const int ClipCount = 8;

    /// <summary>
    /// The curated set, in output order. A clip/facing pair lands on row
    /// <see cref="RowFor(int, int)"/> — <c>index * 3 + facing</c>.
    /// </summary>
    public static ImmutableArray<AnimationClip> Clips { get; } =
    [
        new() { Name = "walk",         SourceColumn = 0,  FrameCount = 3 },
        new() { Name = "idle",         SourceColumn = 1,  FrameCount = 1 },
        new() { Name = "arms_up",      SourceColumn = 3,  FrameCount = 3 },
        new() { Name = "crouch",       SourceColumn = 6,  FrameCount = 1 },
        new() { Name = "jump",         SourceColumn = 6,  FrameCount = 4 },
        new() { Name = "attack",       SourceColumn = 11, FrameCount = 4 },
        new() { Name = "heavy_attack", SourceColumn = 10, FrameCount = 5 },
        new() { Name = "sleep_ko",     SourceColumn = 22, FrameCount = 1 },
    ];

    public static int RowFor(int clipIndex, int facing) => clipIndex * FacingCount + facing;

    /// <summary>
    /// Compass bearing of each source facing row, in row order — south, west, east, north.
    /// </summary>
    /// <remarks>
    /// Four, and no diagonals: none exist in the core pack, either expansion, or the <c>tdsm</c>
    /// variant, and a three-quarter pose cannot be derived from these. Stating the bearings is what
    /// lets <see cref="FacingResolution"/> answer an eight-way heading from four-way art.
    /// </remarks>
    public static ImmutableArray<int> SourceBearings { get; } = [180, 270, 90, 0];

    /// <summary>
    /// Bearings the curated geometry can serve. North is dropped, so it cannot serve 0.
    /// </summary>
    /// <remarks>
    /// Sliced from <see cref="SourceBearings"/> rather than restated: north is the last source row
    /// and <see cref="FacingCount"/> is exactly the count that survives, so the two cannot drift.
    /// </remarks>
    public static ImmutableArray<int> CuratedBearings { get; } = [.. SourceBearings.AsSpan()[..FacingCount]];

    /// <summary>
    /// The source facing row to play for a heading, including headings the art has no frames for.
    /// </summary>
    /// <param name="bearing">A compass heading in degrees, north 0 and east 90.</param>
    /// <returns>A row index into the source geometry, 0 to <see cref="SourceRows"/> - 1.</returns>
    public static int RowForBearing(int bearing) =>
        SourceBearings.IndexOf(FacingResolution.Resolve(bearing, SourceBearings.AsSpan()));
}
