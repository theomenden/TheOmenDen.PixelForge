using Meziantou.Framework;
using TheOmenDen.PixelForge.Core.Baking;

namespace TheOmenDen.PixelForge.Core.Catalog;

/// <summary>
/// One partial file in one pack — the smallest unit the picker selects and the baker composites.
/// <para>
/// Identity is the value itself: <see cref="Slot"/> plus <see cref="Base"/> plus
/// <see cref="Variant"/> names exactly one file, and base names never collide across the three
/// packs (core is <c>hair1-12</c>, expansion 1 continues <c>hair13-21</c>, expansion 2
/// <c>hair22-25</c>, and the same holds for every other numbered slot). That is why this is a
/// <see langword="readonly"/> <see langword="record"/> <see langword="struct"/> with no surrogate
/// id — structural equality already is the identity.
/// </para>
/// </summary>
public readonly record struct AssetPartial
{
    /// <summary>Which character layer this file belongs to, and therefore its draw order.</summary>
    public required AssetSlot Slot { get; init; }

    /// <summary>
    /// Which of the three packs supplied it. Derivable from the name, carried for display.
    /// </summary>
    public required ElementsPack Pack { get; init; }

    /// <summary>The name without its colour-variant suffix, e.g. <c>top11</c> or <c>shield1L</c>.</summary>
    public required string Base { get; init; }

    /// <summary>
    /// The <c>_cN</c> colour variant, or <c>0</c> for the base file. Variants recolour the
    /// garment and leave skin untouched; on heads they are eye colours.
    /// </summary>
    public required int Variant { get; init; }

    /// <summary>Absolute path to the <c>.png</c>.</summary>
    public required FullPath Path { get; init; }

    /// <summary>The file's name on disk, including extension.</summary>
    public string FileName => Variant is 0 ? $"{Base}.png" : $"{Base}_c{Variant}.png";

    /// <summary>
    /// The segment this partial contributes to a baked sheet's name. The underscore is dropped
    /// (<c>top11c5</c>, not <c>top11_c5</c>) because the underscore separates <em>slots</em> in
    /// an output stem, and a segment that contained one would be ambiguous to read back.
    /// </summary>
    public string Stem => Variant is 0 ? Base : $"{Base}c{Variant}";

    /// <summary>How this partial orders against its siblings within <see cref="Slot"/>.</summary>
    public AssetSortKey SortKey => AssetName.SortKey(Base, Variant);
}
