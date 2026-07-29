using System.Collections.Immutable;
using DotNext;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.Core.Baking;

/// <summary>
/// One output sheet: the layers that make it, in back-to-front draw order, and the tone its
/// skin-bearing layers are substituted into.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>Overlays</c> collection any more. It existed to draw hair <em>after</em> a
/// substitution that ran over the flattened assembly, so hair's authored colour survived. Once the
/// substitution moved onto individual layers that problem cannot arise, and the old shape could
/// never have expressed back-hair anyway — it draws below the body, so "after the recolour" and
/// "behind the body" were mutually exclusive.
/// </para>
/// </remarks>
public sealed record SheetRecipe
{
    /// <summary>Output stem, e.g. <c>body-01</c>. The <c>.webp</c> is added when written.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Layers back to front, following the generator's <c>CharacterLayers</c> order — which is
    /// also <see cref="Catalog.AssetSlot"/>'s member order, so a planner can sort by slot.
    /// </summary>
    public required ImmutableArray<AssetLayer> Layers { get; init; }

    /// <summary>
    /// The skin tone to substitute into, applied only to layers whose
    /// <see cref="AssetLayer.IsSkin"/> is set.
    /// <para>
    /// <see cref="Optional{T}"/> rather than a nullable reference so "keep the authored tone" is a
    /// value the type system carries, not a <see langword="null"/> every caller must remember to
    /// check. A hair-only sheet has no tone at all.
    /// </para>
    /// </summary>
    public Optional<SkinRamp> Tone { get; init; } = Optional<SkinRamp>.None;

    /// <summary>
    /// Which geometry to write. Defaults to <see cref="SheetGeometry.Curated"/>, so a recipe that
    /// says nothing cannot silently change the Corvus contract.
    /// </summary>
    public SheetGeometry Geometry { get; init; } = SheetGeometry.Curated;
}
