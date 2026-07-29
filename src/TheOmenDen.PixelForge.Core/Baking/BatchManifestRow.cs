namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One row of <c>sheets.csv</c>: an output file and the partials and tone that produced it.
/// </summary>
/// <remarks>
/// One column per <see cref="Catalog.AssetSlot"/> rather than a packed string, so the file is
/// filterable in a spreadsheet — "every sheet wearing hat4" is a column filter, not a text search.
/// Unused slots default to <see cref="string.Empty"/> and write a blank cell, never the text
/// <c>null</c>, which a spreadsheet would show as data rather than as absence.
/// </remarks>
public sealed record BatchManifestRow
{
    /// <summary>The sheet's stem, without extension.</summary>
    public required string Name { get; init; }

    /// <summary>The file written, including extension.</summary>
    public required string File { get; init; }

    /// <summary>
    /// Which <see cref="SheetGeometry"/> was written. Carried as text so the manifest stays
    /// readable without the enum to hand.
    /// </summary>
    public required string Geometry { get; init; }

    /// <summary>Name of the skin ramp applied, or blank when the sheet carries no skin.</summary>
    public string Tone { get; init; } = string.Empty;

    /// <summary>Stem of the shadow partial, or blank.</summary>
    public string Shadow { get; init; } = string.Empty;

    /// <summary>Stem of the back-extra partial, or blank.</summary>
    public string BackExtra { get; init; } = string.Empty;

    /// <summary>Stem of the back-hair partial, or blank.</summary>
    public string BackHair { get; init; } = string.Empty;

    /// <summary>Stem of the bottom partial.</summary>
    public string Bottom { get; init; } = string.Empty;

    /// <summary>Stem of the top partial.</summary>
    public string Top { get; init; } = string.Empty;

    /// <summary>Stem of the head partial.</summary>
    public string Head { get; init; } = string.Empty;

    /// <summary>Stem of the hair partial, or blank.</summary>
    public string Hair { get; init; } = string.Empty;

    /// <summary>Stem of the front-extra partial, or blank.</summary>
    public string FrontExtra { get; init; } = string.Empty;

    /// <summary>Stem of the hat partial, or blank.</summary>
    public string Hat { get; init; } = string.Empty;

    /// <summary>Stem of the weapon partial, or blank.</summary>
    public string Weapon { get; init; } = string.Empty;
}
