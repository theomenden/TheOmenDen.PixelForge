namespace TheOmenDen.PixelForge.Core.Spritesheets;

/// <summary>
/// One direction's block of walk frames on a Time Fantasy sheet.
/// </summary>
/// <remarks>
/// A <see langword="readonly record struct"/> because these are small, immutable and enumerated in
/// loops — the same reasoning that keeps <c>Pixel</c> and <c>Rgba32</c> off the heap.
/// </remarks>
public readonly record struct TimeFantasyFacing
{
    /// <summary>
    /// The direction's name as a consumer sees it, e.g. <c>south_east</c>.
    /// </summary>
    /// <remarks>
    /// Snake case, matching how <see cref="SheetLayout.Clips"/> already names <c>heavy_attack</c>
    /// and <c>sleep_ko</c>, so one naming convention crosses both packs' manifests.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>
    /// Compass bearing in degrees — north 0, east 90, south 180, west 270.
    /// </summary>
    /// <remarks>
    /// Carried rather than derived from <see cref="Name"/> so the diagonal rule is checkable:
    /// every diagonal is its row's cardinal minus 45. A mislabelled row fails that test instead of
    /// shipping a character that walks one way and faces another.
    /// </remarks>
    public required int Bearing { get; init; }

    /// <summary>Sheet row this direction's frames sit on.</summary>
    public required int Row { get; init; }

    /// <summary>First of the three columns this direction's frames occupy.</summary>
    public required int Column { get; init; }
}
